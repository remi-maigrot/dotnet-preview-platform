namespace Preview.Operator.Diagnostics;

internal static partial class Log
{
    [LoggerMessage(100, LogLevel.Information, "Preview {Name}: {Previous} -> {Phase}")]
    public static partial void PhaseChanged(this ILogger logger, string name, string previous, string phase);

    [LoggerMessage(101, LogLevel.Information, "Started deployment job {Job} in {Namespace}")]
    public static partial void DeploymentStarted(this ILogger logger, string @namespace, string job);

    [LoggerMessage(102, LogLevel.Information, "Preview {Name} expired and is being deleted")]
    public static partial void PreviewExpired(this ILogger logger, string name);

    [LoggerMessage(103, LogLevel.Error, "Reconciliation of {Name} failed (attempt {Attempt}), retrying in {Delay}")]
    public static partial void ReconcileFailed(this ILogger logger, Exception exception, string name, int attempt, TimeSpan delay);

    [LoggerMessage(104, LogLevel.Warning, "Watch on PreviewEnvironments ended, listing again")]
    public static partial void WatchRestarted(this ILogger logger, Exception? exception);

    [LoggerMessage(105, LogLevel.Information, "Became leader ({Identity}); starting the controller")]
    public static partial void StartedLeading(this ILogger logger, string identity);

    [LoggerMessage(106, LogLevel.Warning, "Lost leadership ({Identity}); stopping so a clean instance can take over")]
    public static partial void StoppedLeading(this ILogger logger, string identity);

    [LoggerMessage(107, LogLevel.Information, "Current leader is {Leader}")]
    public static partial void NewLeader(this ILogger logger, string leader);
}
