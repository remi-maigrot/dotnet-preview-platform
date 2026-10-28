using System.Text.Json.Serialization;
using k8s;
using k8s.Models;

namespace Preview.Operator.Crds;

/// <summary>
/// An isolated, short-lived environment for one pull request. Cluster-scoped, because it owns a namespace and
/// Kubernetes only lets cluster-scoped objects own other cluster-scoped objects.
/// </summary>
public sealed class PreviewEnvironment : IKubernetesObject<V1ObjectMeta>
{
    public const string Group = "platform.remimaigrot.dev";
    public const string Version = "v1alpha1";
    public const string KindName = "PreviewEnvironment";
    public const string Plural = "previewenvironments";
    public const string ApiVersionName = Group + "/" + Version;

    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; set; } = ApiVersionName;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = KindName;

    [JsonPropertyName("metadata")]
    public V1ObjectMeta Metadata { get; set; } = new();

    [JsonPropertyName("spec")]
    public PreviewEnvironmentSpec Spec { get; set; } = new();

    [JsonPropertyName("status")]
    public PreviewEnvironmentStatus? Status { get; set; }
}

public sealed class PreviewEnvironmentList : IKubernetesObject<V1ListMeta>, IItems<PreviewEnvironment>
{
    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; set; } = PreviewEnvironment.ApiVersionName;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = PreviewEnvironment.KindName + "List";

    [JsonPropertyName("metadata")]
    public V1ListMeta Metadata { get; set; } = new();

    [JsonPropertyName("items")]
    public IList<PreviewEnvironment> Items { get; set; } = [];
}
