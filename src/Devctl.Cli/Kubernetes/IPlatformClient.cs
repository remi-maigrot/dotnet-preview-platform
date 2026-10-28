using Preview.Operator.Crds;

namespace Devctl.Cli.Kubernetes;

internal sealed record PreviewSummary(int PullRequest, string Name, string Image, string Phase, string? Url, DateTimeOffset? ExpiresAt, DateTimeOffset? CreatedAt, string? Message)
{
    public long Generation { get; init; }

    public long? ObservedGeneration { get; init; }

    /// <summary>The status describes the current spec: the operator has seen the last change.</summary>
    public bool IsUpToDate => ObservedGeneration >= Generation;
}

internal sealed record ComponentHealth(string Name, string Namespace, bool Healthy, string Detail)
{
    public bool Installed { get; init; } = true;
}

/// <summary>What devctl reads and writes through the Kubernetes API (kubectl is only used for streaming logs).</summary>
internal interface IPlatformClient
{
    Task<IReadOnlyList<PreviewSummary>> ListPreviewsAsync(CancellationToken cancellationToken);

    Task<PreviewSummary?> GetPreviewAsync(string name, CancellationToken cancellationToken);

    Task ApplyPreviewAsync(PreviewEnvironment preview, CancellationToken cancellationToken);

    /// <summary>Returns false when there was nothing to delete.</summary>
    Task<bool> DeletePreviewAsync(string name, CancellationToken cancellationToken);

    Task<ComponentHealth> GetDeploymentHealthAsync(string @namespace, string name, CancellationToken cancellationToken);

    /// <summary>The preview's status and the pods, jobs and events of its namespace. Null when the preview does not exist.</summary>
    Task<Diagnostics.PreviewSnapshot?> GetSnapshotAsync(string name, CancellationToken cancellationToken);

    /// <summary>The last lines of a container's log; empty when there is none (container not started yet).</summary>
    Task<string> GetLogAsync(string @namespace, string pod, string container, bool previous, int tailLines, CancellationToken cancellationToken);
}
