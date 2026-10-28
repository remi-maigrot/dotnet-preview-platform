using System.Net;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Preview.Operator.Crds;
using Preview.Operator.Reconciliation;

namespace Preview.Operator.Kubernetes;

/// <summary>Adapter between the reconciler and the Kubernetes API. No decisions here, only API calls.</summary>
internal sealed class KubernetesClusterGateway(IKubernetes client, TimeProvider clock) : IClusterGateway
{
    private const string Group = PreviewEnvironment.Group;
    private const string Version = PreviewEnvironment.Version;
    private const string Plural = PreviewEnvironment.Plural;

    public async Task<PreviewEnvironment?> GetPreviewAsync(string name, CancellationToken cancellationToken) =>
        await NullIfNotFound(() => client.CustomObjects.GetClusterCustomObjectAsync<PreviewEnvironment>(
            Group, Version, Plural, name, cancellationToken));

    public async Task<IReadOnlyList<PreviewEnvironment>> ListPreviewsAsync(CancellationToken cancellationToken)
    {
        var list = await client.CustomObjects.ListClusterCustomObjectAsync<PreviewEnvironmentList>(
            Group, Version, Plural, cancellationToken: cancellationToken);
        return [.. list.Items];
    }

    public Task ApplyAsync(IKubernetesObject<V1ObjectMeta> resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var patch = new V1Patch(resource, V1Patch.PatchType.ApplyPatch);
        var name = resource.Metadata.Name;
        var ns = resource.Metadata.NamespaceProperty;
        const string Manager = PreviewManifests.FieldManager;

        // Server-side apply with force: the operator owns these fields and takes them back if someone edits them.
        return resource switch
        {
            V1Namespace => client.CoreV1.PatchNamespaceAsync(patch, name, fieldManager: Manager, force: true, cancellationToken: cancellationToken),
            V1ResourceQuota => client.CoreV1.PatchNamespacedResourceQuotaAsync(patch, name, ns, fieldManager: Manager, force: true, cancellationToken: cancellationToken),
            V1LimitRange => client.CoreV1.PatchNamespacedLimitRangeAsync(patch, name, ns, fieldManager: Manager, force: true, cancellationToken: cancellationToken),
            V1Secret => client.CoreV1.PatchNamespacedSecretAsync(patch, name, ns, fieldManager: Manager, force: true, cancellationToken: cancellationToken),
            V1ConfigMap => client.CoreV1.PatchNamespacedConfigMapAsync(patch, name, ns, fieldManager: Manager, force: true, cancellationToken: cancellationToken),
            V1Service => client.CoreV1.PatchNamespacedServiceAsync(patch, name, ns, fieldManager: Manager, force: true, cancellationToken: cancellationToken),
            V1ServiceAccount => client.CoreV1.PatchNamespacedServiceAccountAsync(patch, name, ns, fieldManager: Manager, force: true, cancellationToken: cancellationToken),
            V1NetworkPolicy => client.NetworkingV1.PatchNamespacedNetworkPolicyAsync(patch, name, ns, fieldManager: Manager, force: true, cancellationToken: cancellationToken),
            V1StatefulSet => client.AppsV1.PatchNamespacedStatefulSetAsync(patch, name, ns, fieldManager: Manager, force: true, cancellationToken: cancellationToken),
            V1RoleBinding => client.RbacAuthorizationV1.PatchNamespacedRoleBindingAsync(patch, name, ns, fieldManager: Manager, force: true, cancellationToken: cancellationToken),
            V1Job => client.BatchV1.PatchNamespacedJobAsync(patch, name, ns, fieldManager: Manager, force: true, cancellationToken: cancellationToken),
            _ => throw new NotSupportedException($"No apply mapping for {resource.GetType().Name}."),
        };
    }

    public async Task<V1Secret?> GetSecretAsync(string @namespace, string name, CancellationToken cancellationToken) =>
        await NullIfNotFound(() => client.CoreV1.ReadNamespacedSecretAsync(name, @namespace, cancellationToken: cancellationToken));

    public async Task CreateSecretIfMissingAsync(V1Secret secret, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secret);

        try
        {
            await client.CoreV1.CreateNamespacedSecretAsync(secret, secret.Metadata.NamespaceProperty, cancellationToken: cancellationToken);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Conflict)
        {
            // Already there: generated credentials are never rotated behind the database's back.
        }
    }

    public async Task<V1StatefulSet?> GetStatefulSetAsync(string @namespace, string name, CancellationToken cancellationToken) =>
        await NullIfNotFound(() => client.AppsV1.ReadNamespacedStatefulSetAsync(name, @namespace, cancellationToken: cancellationToken));

    public async Task<V1Job?> GetJobAsync(string @namespace, string name, CancellationToken cancellationToken) =>
        await NullIfNotFound(() => client.BatchV1.ReadNamespacedJobAsync(name, @namespace, cancellationToken: cancellationToken));

    public async Task<V1Deployment?> GetDeploymentAsync(string @namespace, string name, CancellationToken cancellationToken) =>
        await NullIfNotFound(() => client.AppsV1.ReadNamespacedDeploymentAsync(name, @namespace, cancellationToken: cancellationToken));

    public async Task<bool> NamespaceExistsAsync(string name, CancellationToken cancellationToken) =>
        await NullIfNotFound(() => client.CoreV1.ReadNamespaceAsync(name, cancellationToken: cancellationToken)) is not null;

    public async Task DeleteNamespaceAsync(string name, CancellationToken cancellationToken) =>
        await NullIfNotFound(() => client.CoreV1.DeleteNamespaceAsync(
            name, new V1DeleteOptions { PropagationPolicy = "Foreground" }, cancellationToken: cancellationToken));

    public async Task DeletePreviewAsync(string name, CancellationToken cancellationToken) =>
        await NullIfNotFound(() => client.CustomObjects.DeleteClusterCustomObjectAsync(
            Group, Version, Plural, name, cancellationToken: cancellationToken));

    public Task SetFinalizerAsync(PreviewEnvironment preview, bool present, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);

        var finalizers = (preview.Metadata.Finalizers ?? []).Where(f => f != PreviewReconciler.Finalizer).ToList();
        if (present)
        {
            finalizers.Add(PreviewReconciler.Finalizer);
        }

        // resourceVersion makes this an optimistic update: if the object changed meanwhile, the call fails and the
        // reconciliation is retried with fresh data instead of overwriting someone else's finalizer.
        var patch = new
        {
            metadata = new { finalizers, resourceVersion = preview.Metadata.ResourceVersion },
        };
        return client.CustomObjects.PatchClusterCustomObjectAsync(
            new V1Patch(patch, V1Patch.PatchType.MergePatch), Group, Version, Plural, preview.Metadata.Name,
            cancellationToken: cancellationToken);
    }

    public Task UpdateStatusAsync(PreviewEnvironment preview, PreviewEnvironmentStatus status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);

        return client.CustomObjects.PatchClusterCustomObjectStatusAsync(
            new V1Patch(new { status }, V1Patch.PatchType.MergePatch), Group, Version, Plural, preview.Metadata.Name,
            cancellationToken: cancellationToken);
    }

    public async Task RecordEventAsync(PreviewEnvironment preview, string type, string reason, string message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);

        var now = clock.GetUtcNow().UtcDateTime;
        var @event = new Corev1Event
        {
            Metadata = new V1ObjectMeta { GenerateName = $"{preview.Metadata.Name}.", NamespaceProperty = "default" },
            InvolvedObject = new V1ObjectReference
            {
                ApiVersion = preview.ApiVersion,
                Kind = preview.Kind,
                Name = preview.Metadata.Name,
                Uid = preview.Metadata.Uid,
                ResourceVersion = preview.Metadata.ResourceVersion,
            },
            Type = type,
            Reason = reason,
            Message = message,
            Source = new V1EventSource { Component = PreviewManifests.FieldManager },
            ReportingComponent = PreviewManifests.FieldManager,
            ReportingInstance = Environment.MachineName,
            FirstTimestamp = now,
            LastTimestamp = now,
            Count = 1,
        };

        // Events of cluster-scoped objects are recorded in the "default" namespace, as kubectl expects.
        await client.CoreV1.CreateNamespacedEventAsync(@event, "default", cancellationToken: cancellationToken);
    }

    private static async Task<T?> NullIfNotFound<T>(Func<Task<T>> call)
        where T : class
    {
        try
        {
            return await call();
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
