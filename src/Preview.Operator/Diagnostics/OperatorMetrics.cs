using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Preview.Operator.Crds;

namespace Preview.Operator.Diagnostics;

/// <summary>Operator metrics, exported on /metrics (preview_operator_* and preview_environments).</summary>
internal sealed class OperatorMetrics
{
    public const string MeterName = "Preview.Operator";

    private readonly Counter<long> _reconciliations;
    private readonly Histogram<double> _duration;
    private readonly ConcurrentDictionary<string, string> _phases = new();
    private int _isLeader;
    private int _queueDepth;

    public OperatorMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(MeterName);
        _reconciliations = meter.CreateCounter<long>("preview_operator_reconciliations", "{reconciliation}",
            "Reconciliations by result (success, error).");
        _duration = meter.CreateHistogram<double>("preview_operator_reconciliation_duration", "s",
            "Time spent reconciling one PreviewEnvironment.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.01, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10] });
        meter.CreateObservableGauge("preview_environments", ObservePhases, "{environment}",
            "PreviewEnvironments known to the operator, by phase.");
        meter.CreateObservableGauge("preview_operator_leader", () => Volatile.Read(ref _isLeader), "1",
            "1 when this replica holds the leader lease.");
        meter.CreateObservableGauge("preview_operator_workqueue_depth", () => Volatile.Read(ref _queueDepth), "{item}",
            "Items waiting to be reconciled.");
    }

    public void Reconciled(TimeSpan duration, bool success)
    {
        var result = new KeyValuePair<string, object?>("result", success ? "success" : "error");
        _reconciliations.Add(1, result);
        _duration.Record(duration.TotalSeconds, result);
    }

    public void PhaseObserved(string name, string phase) => _phases[name] = phase;

    public void Forget(string name) => _phases.TryRemove(name, out _);

    public void SetLeader(bool isLeader) => Volatile.Write(ref _isLeader, isLeader ? 1 : 0);

    public void SetQueueDepth(int depth) => Volatile.Write(ref _queueDepth, depth);

    private IEnumerable<Measurement<int>> ObservePhases()
    {
        var counts = _phases.Values.GroupBy(p => p).ToDictionary(g => g.Key, g => g.Count());
        foreach (var phase in PreviewPhase.All)
        {
            yield return new Measurement<int>(counts.GetValueOrDefault(phase), new KeyValuePair<string, object?>("phase", phase));
        }
    }
}
