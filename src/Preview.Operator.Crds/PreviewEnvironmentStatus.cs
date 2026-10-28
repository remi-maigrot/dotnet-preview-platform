using System.Text.Json.Serialization;
using k8s.Models;

namespace Preview.Operator.Crds;

public sealed class PreviewEnvironmentStatus
{
    [JsonPropertyName("phase")]
    public string Phase { get; set; } = PreviewPhase.Pending;

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("observedGeneration")]
    public long? ObservedGeneration { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("conditions")]
    public IList<V1Condition> Conditions { get; set; } = [];
}

public static class PreviewPhase
{
    public const string Pending = "Pending";
    public const string Provisioning = "Provisioning";
    public const string Ready = "Ready";
    public const string Failed = "Failed";
    public const string Expired = "Expired";

    public static readonly IReadOnlyList<string> All = [Pending, Provisioning, Ready, Failed, Expired];
}

/// <summary>Condition types, following the Kubernetes API conventions (positive polarity, CamelCase).</summary>
public static class PreviewConditions
{
    public const string NamespaceReady = "NamespaceReady";
    public const string DatabaseReady = "DatabaseReady";
    public const string ApplicationDeployed = "ApplicationDeployed";
    public const string Ready = "Ready";
}
