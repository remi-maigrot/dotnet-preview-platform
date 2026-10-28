using k8s;
using k8s.Models;
using Preview.Operator.Crds;

namespace Preview.Operator.Reconciliation;

/// <summary>
/// Everything the reconciler needs from the cluster. Kept narrow on purpose: the reconciler is tested against an
/// in-memory fake, and the real implementation is a thin adapter over the Kubernetes client.
/// </summary>
internal interface IClusterGateway
{
    Task<PreviewEnvironment?> GetPreviewAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<PreviewEnvironment>> ListPreviewsAsync(CancellationToken cancellationToken);

    /// <summary>Server-side apply: creates or updates the object, owning only the fields it sets.</summary>
    Task ApplyAsync(IKubernetesObject<V1ObjectMeta> resource, CancellationToken cancellationToken);

    Task<V1Secret?> GetSecretAsync(string @namespace, string name, CancellationToken cancellationToken);

    /// <summary>Creates the secret unless it exists. Generated credentials must never be overwritten.</summary>
    Task CreateSecretIfMissingAsync(V1Secret secret, CancellationToken cancellationToken);

    Task<V1StatefulSet?> GetStatefulSetAsync(string @namespace, string name, CancellationToken cancellationToken);

    Task<V1Job?> GetJobAsync(string @namespace, string name, CancellationToken cancellationToken);

    Task<V1Deployment?> GetDeploymentAsync(string @namespace, string name, CancellationToken cancellationToken);

    Task<bool> NamespaceExistsAsync(string name, CancellationToken cancellationToken);

    Task DeleteNamespaceAsync(string name, CancellationToken cancellationToken);

    Task DeletePreviewAsync(string name, CancellationToken cancellationToken);

    Task SetFinalizerAsync(PreviewEnvironment preview, bool present, CancellationToken cancellationToken);

    Task UpdateStatusAsync(PreviewEnvironment preview, PreviewEnvironmentStatus status, CancellationToken cancellationToken);

    Task RecordEventAsync(PreviewEnvironment preview, string type, string reason, string message, CancellationToken cancellationToken);
}
