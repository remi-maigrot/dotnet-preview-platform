using System.Net;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Preview.Operator.Crds;

namespace Devctl.Cli.Kubernetes;

internal sealed class KubernetesPlatformClient(IKubernetes client) : IPlatformClient
{
    public static IPlatformClient ForContext(string context) =>
        new KubernetesPlatformClient(new k8s.Kubernetes(KubernetesClientConfiguration.BuildConfigFromConfigFile(currentContext: context)));

    public async Task<IReadOnlyList<PreviewSummary>> ListPreviewsAsync(CancellationToken cancellationToken)
    {
        var list = await client.CustomObjects.ListClusterCustomObjectAsync<PreviewEnvironmentList>(
            PreviewEnvironment.Group, PreviewEnvironment.Version, PreviewEnvironment.Plural, cancellationToken: cancellationToken);
        return [.. list.Items.Select(Summarise).OrderBy(p => p.PullRequest)];
    }

    public async Task<PreviewSummary?> GetPreviewAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var preview = await client.CustomObjects.GetClusterCustomObjectAsync<PreviewEnvironment>(
                PreviewEnvironment.Group, PreviewEnvironment.Version, PreviewEnvironment.Plural, name, cancellationToken);
            return Summarise(preview);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task ApplyPreviewAsync(PreviewEnvironment preview, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);

        return client.CustomObjects.PatchClusterCustomObjectAsync(
            new V1Patch(preview, V1Patch.PatchType.ApplyPatch),
            PreviewEnvironment.Group, PreviewEnvironment.Version, PreviewEnvironment.Plural, preview.Metadata.Name,
            fieldManager: "devctl", force: true, cancellationToken: cancellationToken);
    }

    public async Task<bool> DeletePreviewAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            await client.CustomObjects.DeleteClusterCustomObjectAsync(
                PreviewEnvironment.Group, PreviewEnvironment.Version, PreviewEnvironment.Plural, name,
                cancellationToken: cancellationToken);
            return true;
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<ComponentHealth> GetDeploymentHealthAsync(string @namespace, string name, CancellationToken cancellationToken)
    {
        try
        {
            var deployment = await client.AppsV1.ReadNamespacedDeploymentAsync(name, @namespace, cancellationToken: cancellationToken);
            var ready = deployment.Status?.ReadyReplicas ?? 0;
            var desired = deployment.Spec?.Replicas ?? 1;
            return new ComponentHealth(name, @namespace, ready >= 1, $"{ready}/{desired} ready");
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return new ComponentHealth(name, @namespace, false, "not installed") { Installed = false };
        }
    }

    private static PreviewSummary Summarise(PreviewEnvironment preview) => new(
        preview.Spec.PullRequest,
        preview.Metadata.Name,
        preview.Spec.Image,
        preview.Status?.Phase ?? PreviewPhase.Pending,
        preview.Status?.Url,
        preview.Status?.ExpiresAt,
        preview.Metadata.CreationTimestamp is { } created ? new DateTimeOffset(DateTime.SpecifyKind(created, DateTimeKind.Utc)) : null,
        preview.Status?.Message)
    {
        Generation = preview.Metadata.Generation ?? 0,
        ObservedGeneration = preview.Status?.ObservedGeneration,
    };
}
