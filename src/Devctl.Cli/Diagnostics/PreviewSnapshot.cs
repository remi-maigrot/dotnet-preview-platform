namespace Devctl.Cli.Diagnostics;

/// <summary>Everything devctl reads to explain a preview: its status, and the state of its namespace.</summary>
internal sealed record PreviewSnapshot(
    string Name,
    string Namespace,
    string Phase,
    string? Message,
    string Image,
    IReadOnlyList<ConditionInfo> Conditions,
    IReadOnlyList<PodInfo> Pods,
    IReadOnlyList<JobInfo> Jobs,
    IReadOnlyList<EventInfo> Events);

internal sealed record ConditionInfo(string Type, string Status, string? Reason, string? Message);

internal sealed record PodInfo(string Name, string Phase, IReadOnlyList<ContainerInfo> Containers)
{
    /// <summary>Pods created by a Job, as opposed to the API and the database.</summary>
    public string? JobName { get; init; }
}

internal sealed record ContainerInfo(string Name, bool Ready, int Restarts, string? Waiting, string? WaitingMessage)
{
    public string? LastTerminationReason { get; init; }

    public int? LastExitCode { get; init; }

    public string? TerminatedReason { get; init; }

    public int? TerminatedExitCode { get; init; }
}

internal sealed record JobInfo(string Name, int Succeeded, int Failed, bool Complete, string? FailureReason, string? FailureMessage);

internal sealed record EventInfo(DateTimeOffset? Time, string Type, string Reason, string Object, string Message, int Count);

/// <summary>The tail of one container's log, current or from the run before the last restart.</summary>
internal sealed record LogExcerpt(string Pod, string Container, bool Previous, string Text);
