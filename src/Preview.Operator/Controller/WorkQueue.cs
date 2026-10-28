using System.Threading.Channels;

namespace Preview.Operator.Controller;

/// <summary>
/// A work queue with the guarantees of client-go's: a key is never processed by two workers at once, a key added
/// while it is being processed is processed again afterwards, duplicates collapse, and failures back off
/// exponentially per key.
/// </summary>
internal sealed class WorkQueue : IDisposable
{
    private readonly Lock _lock = new();
    private readonly Channel<string> _ready = Channel.CreateUnbounded<string>();
    private readonly HashSet<string> _dirty = [];
    private readonly HashSet<string> _processing = [];
    private readonly Dictionary<string, (DateTimeOffset Due, ITimer Timer)> _delayed = [];
    private readonly Dictionary<string, int> _failures = [];
    private readonly TimeProvider _clock;
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;

    public WorkQueue(TimeProvider clock, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        _clock = clock;
        _baseDelay = baseDelay;
        _maxDelay = maxDelay;
    }

    /// <summary>Keys waiting to be processed, delayed ones included.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _dirty.Count + _delayed.Keys.Count(k => !_dirty.Contains(k));
            }
        }
    }

    public void Add(string key)
    {
        lock (_lock)
        {
            if (!_dirty.Add(key))
            {
                return;
            }

            if (!_processing.Contains(key))
            {
                _ready.Writer.TryWrite(key);
            }
        }
    }

    /// <summary>Adds the key after <paramref name="delay"/>. If the key is already scheduled earlier, the earlier time wins.</summary>
    public void AddAfter(string key, TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            Add(key);
            return;
        }

        lock (_lock)
        {
            var due = _clock.GetUtcNow() + delay;
            if (_delayed.TryGetValue(key, out var scheduled))
            {
                if (scheduled.Due <= due)
                {
                    return;
                }

                scheduled.Timer.Dispose();
            }

            var timer = _clock.CreateTimer(_ => Fire(key), null, delay, Timeout.InfiniteTimeSpan);
            _delayed[key] = (due, timer);
        }
    }

    /// <summary>Requeues a key that failed, waiting longer after each consecutive failure.</summary>
    public TimeSpan AddRateLimited(string key)
    {
        TimeSpan delay;
        lock (_lock)
        {
            var failures = _failures.GetValueOrDefault(key) + 1;
            _failures[key] = failures;
            var exponential = _baseDelay.TotalMilliseconds * Math.Pow(2, Math.Min(failures - 1, 30));
            delay = TimeSpan.FromMilliseconds(Math.Min(exponential, _maxDelay.TotalMilliseconds));
        }

        AddAfter(key, delay);
        return delay;
    }

    public int Failures(string key)
    {
        lock (_lock)
        {
            return _failures.GetValueOrDefault(key);
        }
    }

    /// <summary>Resets the backoff of a key after a successful reconciliation.</summary>
    public void Forget(string key)
    {
        lock (_lock)
        {
            _failures.Remove(key);
        }
    }

    public async ValueTask<string> TakeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var key = await _ready.Reader.ReadAsync(cancellationToken);
            lock (_lock)
            {
                // A key can be written twice to the channel when it is re-added right after Done; skip the stale copy.
                if (_processing.Contains(key) || !_dirty.Remove(key))
                {
                    continue;
                }

                _processing.Add(key);
                return key;
            }
        }
    }

    public void Done(string key)
    {
        lock (_lock)
        {
            _processing.Remove(key);
            if (_dirty.Contains(key))
            {
                _ready.Writer.TryWrite(key);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var (_, timer) in _delayed.Values)
            {
                timer.Dispose();
            }

            _delayed.Clear();
            _ready.Writer.TryComplete();
        }
    }

    private void Fire(string key)
    {
        lock (_lock)
        {
            if (_delayed.Remove(key, out var scheduled))
            {
                scheduled.Timer.Dispose();
            }
        }

        Add(key);
    }
}
