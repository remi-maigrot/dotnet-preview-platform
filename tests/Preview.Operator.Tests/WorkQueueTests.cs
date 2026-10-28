using Microsoft.Extensions.Time.Testing;
using Preview.Operator.Controller;

namespace Preview.Operator.Tests;

public sealed class WorkQueueTests : IDisposable
{
    private readonly FakeTimeProvider _clock = new();
    private readonly WorkQueue _queue;

    public WorkQueueTests() => _queue = new WorkQueue(_clock, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));

    [Fact]
    public async Task Duplicates_AreCollapsed()
    {
        _queue.Add("a");
        _queue.Add("a");
        _queue.Add("b");

        (await TakeAsync()).ShouldBe("a");
        (await TakeAsync()).ShouldBe("b");
        _queue.Count.ShouldBe(0);
    }

    [Fact]
    public async Task KeyAddedWhileProcessing_IsProcessedAgainAfterDone()
    {
        _queue.Add("a");
        var key = await TakeAsync();
        _queue.Add("a");

        TryTake().ShouldBeNull("the same key is never handed to two workers");
        _queue.Done(key);

        (await TakeAsync()).ShouldBe("a");
    }

    [Fact]
    public async Task Pending_CountsKeysDueNowOnly()
    {
        _queue.AddAfter("expires-later", TimeSpan.FromHours(1));
        _queue.Add("a");
        _queue.Add("b");
        _queue.Pending.ShouldBe(2);

        var key = await TakeAsync();
        _queue.Pending.ShouldBe(1, "a key being processed is not waiting");
        _queue.Count.ShouldBe(2);

        _queue.Done(key);
        await TakeAsync();
        _queue.Pending.ShouldBe(0);
        _queue.Count.ShouldBe(1, "the scheduled key is still there");
    }

    [Fact]
    public async Task AddAfter_WaitsForTheDelay()
    {
        _queue.AddAfter("a", TimeSpan.FromSeconds(10));

        _clock.Advance(TimeSpan.FromSeconds(9));
        TryTake().ShouldBeNull();
        _clock.Advance(TimeSpan.FromSeconds(1));

        (await TakeAsync()).ShouldBe("a");
    }

    [Fact]
    public async Task AddAfter_EarlierScheduleWins()
    {
        _queue.AddAfter("a", TimeSpan.FromSeconds(5));
        _queue.AddAfter("a", TimeSpan.FromMinutes(5));

        _clock.Advance(TimeSpan.FromSeconds(5));

        (await TakeAsync()).ShouldBe("a");
    }

    [Fact]
    public void AddRateLimited_BacksOffExponentiallyUpToTheCap()
    {
        var delays = Enumerable.Range(0, 7).Select(_ => _queue.AddRateLimited("a").TotalSeconds).ToList();

        delays.ShouldBe([1, 2, 4, 8, 16, 30, 30]);
        _queue.Failures("a").ShouldBe(7);
    }

    [Fact]
    public void Forget_ResetsTheBackoff()
    {
        _queue.AddRateLimited("a");
        _queue.AddRateLimited("a");

        _queue.Forget("a");

        _queue.AddRateLimited("a").ShouldBe(TimeSpan.FromSeconds(1));
    }

    public void Dispose() => _queue.Dispose();

    private async Task<string> TakeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await _queue.TakeAsync(timeout.Token);
    }

    private string? TryTake()
    {
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        try
        {
            return _queue.TakeAsync(cancelled.Token).AsTask().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
