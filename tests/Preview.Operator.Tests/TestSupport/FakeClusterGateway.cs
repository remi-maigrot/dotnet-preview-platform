using k8s;
using k8s.Models;
using Preview.Operator.Crds;
using Preview.Operator.Reconciliation;

namespace Preview.Operator.Tests.TestSupport;

/// <summary>An in-memory cluster: stores what the reconciler applies and lets tests play the role of the controllers.</summary>
internal sealed class FakeClusterGateway : IClusterGateway
{
    private readonly Dictionary<string, IKubernetesObject<V1ObjectMeta>> _objects = [];
    private readonly Dictionary<string, PreviewEnvironment> _previews = [];

    public List<string> ApplyLog { get; } = [];

    public List<(string Type, string Reason, string Message)> Events { get; } = [];

    public List<string> DeletedNamespaces { get; } = [];

    public List<string> DeletedPreviews { get; } = [];

    public int SecretsCreated { get; private set; }

    public string SecretValue(string ns, string name, string key) =>
        System.Text.Encoding.UTF8.GetString(Get<V1Secret>("Secret", ns, name)!.Data[key]);

    public void Add(PreviewEnvironment preview) => _previews[preview.Metadata.Name] = preview;

    public PreviewEnvironment Preview(string name) => _previews[name];

    public T? Get<T>(string kind, string? ns, string name)
        where T : class, IKubernetesObject<V1ObjectMeta> =>
        _objects.TryGetValue(Key(kind, ns, name), out var value) ? (T)value : null;

    public IEnumerable<V1Job> Jobs(string ns) =>
        _objects.Values.OfType<V1Job>().Where(j => j.Metadata.NamespaceProperty == ns);

    public void MarkDatabaseReady(string ns) =>
        Get<V1StatefulSet>("StatefulSet", ns, PreviewManifests.DatabaseName)!.Status = new V1StatefulSetStatus { ReadyReplicas = 1 };

    public void CompleteJob(string ns, string name, bool succeeded = true) =>
        Get<V1Job>("Job", ns, name)!.Status = new V1JobStatus
        {
            Conditions = [new V1JobCondition { Type = succeeded ? "Complete" : "Failed", Status = "True" }],
        };

    public void MarkApiAvailable(string ns) =>
        _objects[Key("Deployment", ns, PreviewManifests.ApiDeploymentName)] = new V1Deployment
        {
            Metadata = new V1ObjectMeta { Name = PreviewManifests.ApiDeploymentName, NamespaceProperty = ns },
            Status = new V1DeploymentStatus { AvailableReplicas = 1 },
        };

    public void RemoveNamespace(string name) => _objects.Remove(Key("Namespace", null, name));

    public Task<PreviewEnvironment?> GetPreviewAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(_previews.GetValueOrDefault(name));

    public Task<IReadOnlyList<PreviewEnvironment>> ListPreviewsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PreviewEnvironment>>([.. _previews.Values]);

    public Task ApplyAsync(IKubernetesObject<V1ObjectMeta> resource, CancellationToken cancellationToken)
    {
        var key = Key(resource.Kind, resource.Metadata.NamespaceProperty, resource.Metadata.Name);
        ApplyLog.Add(key);

        // Like server-side apply, the spec is replaced but the status written by other controllers is kept.
        var status = _objects.GetValueOrDefault(key) switch
        {
            V1StatefulSet s => (object?)s.Status,
            V1Job j => j.Status,
            _ => null,
        };
        _objects[key] = resource;
        switch (resource)
        {
            case V1StatefulSet s when status is V1StatefulSetStatus st:
                s.Status = st;
                break;
            case V1Job j when status is V1JobStatus js:
                j.Status = js;
                break;
        }

        return Task.CompletedTask;
    }

    public Task<V1Secret?> GetSecretAsync(string @namespace, string name, CancellationToken cancellationToken) =>
        Task.FromResult(Get<V1Secret>("Secret", @namespace, name));

    public Task CreateSecretIfMissingAsync(V1Secret secret, CancellationToken cancellationToken)
    {
        var key = Key("Secret", secret.Metadata.NamespaceProperty, secret.Metadata.Name);
        if (!_objects.ContainsKey(key))
        {
            _objects[key] = secret;
            SecretsCreated++;
        }

        return Task.CompletedTask;
    }

    public Task<V1StatefulSet?> GetStatefulSetAsync(string @namespace, string name, CancellationToken cancellationToken) =>
        Task.FromResult(Get<V1StatefulSet>("StatefulSet", @namespace, name));

    public Task<V1Job?> GetJobAsync(string @namespace, string name, CancellationToken cancellationToken) =>
        Task.FromResult(Get<V1Job>("Job", @namespace, name));

    public Task<V1Deployment?> GetDeploymentAsync(string @namespace, string name, CancellationToken cancellationToken) =>
        Task.FromResult(Get<V1Deployment>("Deployment", @namespace, name));

    public Task<bool> NamespaceExistsAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(_objects.ContainsKey(Key("Namespace", null, name)));

    public Task DeleteNamespaceAsync(string name, CancellationToken cancellationToken)
    {
        DeletedNamespaces.Add(name);
        return Task.CompletedTask;
    }

    public Task DeletePreviewAsync(string name, CancellationToken cancellationToken)
    {
        DeletedPreviews.Add(name);
        return Task.CompletedTask;
    }

    public Task SetFinalizerAsync(PreviewEnvironment preview, bool present, CancellationToken cancellationToken)
    {
        var finalizers = (preview.Metadata.Finalizers ?? []).Where(f => f != PreviewReconciler.Finalizer).ToList();
        if (present)
        {
            finalizers.Add(PreviewReconciler.Finalizer);
        }

        preview.Metadata.Finalizers = finalizers;
        return Task.CompletedTask;
    }

    public Task UpdateStatusAsync(PreviewEnvironment preview, PreviewEnvironmentStatus status, CancellationToken cancellationToken)
    {
        preview.Status = status;
        return Task.CompletedTask;
    }

    public Task RecordEventAsync(PreviewEnvironment preview, string type, string reason, string message, CancellationToken cancellationToken)
    {
        Events.Add((type, reason, message));
        return Task.CompletedTask;
    }

    private static string Key(string kind, string? ns, string name) => $"{kind}/{ns}/{name}";
}
