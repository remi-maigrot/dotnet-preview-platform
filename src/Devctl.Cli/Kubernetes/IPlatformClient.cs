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
}
