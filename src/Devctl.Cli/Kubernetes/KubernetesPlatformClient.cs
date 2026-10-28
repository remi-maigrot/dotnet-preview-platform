using System.Net;
using Devctl.Cli.Diagnostics;
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

    public async Task<PreviewSnapshot?> GetSnapshotAsync(string name, CancellationToken cancellationToken)
    {
        PreviewEnvironment preview;
        try
        {
            preview = await client.CustomObjects.GetClusterCustomObjectAsync<PreviewEnvironment>(
                PreviewEnvironment.Group, PreviewEnvironment.Version, PreviewEnvironment.Plural, name, cancellationToken);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var ns = preview.Status?.Namespace ?? name;
        var pods = await NamespacedOrEmpty(() => client.CoreV1.ListNamespacedPodAsync(ns, cancellationToken: cancellationToken), l => l.Items);
        var jobs = await NamespacedOrEmpty(() => client.BatchV1.ListNamespacedJobAsync(ns, cancellationToken: cancellationToken), l => l.Items);
        var events = await NamespacedOrEmpty(() => client.CoreV1.ListNamespacedEventAsync(ns, cancellationToken: cancellationToken), l => l.Items);
        // Events about the PreviewEnvironment itself are recorded in "default": it is cluster-scoped.
        var own = await client.CoreV1.ListNamespacedEventAsync(
            "default", fieldSelector: $"involvedObject.name={name}", cancellationToken: cancellationToken);

        return new PreviewSnapshot(
            name,
            ns,
            preview.Status?.Phase ?? PreviewPhase.Pending,
            preview.Status?.Message,
            preview.Spec.Image,
            [.. (preview.Status?.Conditions ?? []).Select(c => new ConditionInfo(c.Type, c.Status, c.Reason, c.Message))],
            [.. pods.Select(ToPod)],
            [.. jobs.Select(ToJob)],
            [.. events.Concat(own.Items).Select(ToEvent)]);
    }

    public async Task<string> GetLogAsync(string @namespace, string pod, string container, bool previous, int tailLines, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = await client.CoreV1.ReadNamespacedPodLogAsync(
                pod, @namespace, container: container, previous: previous, tailLines: tailLines, cancellationToken: cancellationToken);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            // 400: the container has not started yet, or has no previous run.
            return string.Empty;
        }
    }

    private static async Task<IEnumerable<TItem>> NamespacedOrEmpty<TList, TItem>(Func<Task<TList>> list, Func<TList, IEnumerable<TItem>> items)
    {
        try
        {
            return items(await list());
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }
    }

    private static PodInfo ToPod(V1Pod pod) => new(
        pod.Metadata.Name,
        pod.Status?.Phase ?? "Unknown",
        [.. (pod.Status?.InitContainerStatuses ?? []).Concat(pod.Status?.ContainerStatuses ?? []).Select(c => new ContainerInfo(
            c.Name, c.Ready, c.RestartCount, c.State?.Waiting?.Reason, c.State?.Waiting?.Message)
        {
            LastTerminationReason = c.LastState?.Terminated?.Reason,
            LastExitCode = c.LastState?.Terminated?.ExitCode,
            TerminatedReason = c.State?.Terminated?.Reason,
            TerminatedExitCode = c.State?.Terminated?.ExitCode,
        })])
    {
        JobName = pod.Metadata.Labels?.TryGetValue("job-name", out var job) == true ? job : null,
    };

    private static JobInfo ToJob(V1Job job)
    {
        var failed = job.Status?.Conditions?.FirstOrDefault(c => c.Type == "Failed" && c.Status == "True");
        var complete = job.Status?.Conditions?.Any(c => c.Type == "Complete" && c.Status == "True") == true;
        return new JobInfo(job.Metadata.Name, job.Status?.Succeeded ?? 0, job.Status?.Failed ?? 0, complete, failed?.Reason, failed?.Message);
    }

    private static EventInfo ToEvent(Corev1Event e)
    {
        var time = e.LastTimestamp ?? e.EventTime ?? e.FirstTimestamp;
        return new EventInfo(
            time is { } t ? new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)) : null,
            e.Type ?? "Normal",
            e.Reason ?? string.Empty,
            $"{e.InvolvedObject?.Kind}/{e.InvolvedObject?.Name}",
            e.Message ?? string.Empty,
            e.Count ?? 1);
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
