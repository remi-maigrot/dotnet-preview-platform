using System.ComponentModel.DataAnnotations;

namespace Preview.Operator;

/// <summary>Bound from the <c>Operator</c> section; set through environment variables in deploy/operator.</summary>
internal sealed class OperatorOptions
{
    public const string SectionName = "Operator";

    /// <summary>Previews are served at <c>pr-&lt;n&gt;.{PreviewDomain}</c>.</summary>
    [Required]
    [RegularExpression(@"^[a-z0-9]([a-z0-9.-]*[a-z0-9])?$")]
    public string PreviewDomain { get; set; } = "preview.localtest.me";

    [RegularExpression("^https?$")]
    public string UrlScheme { get; set; } = "http";

    /// <summary>Port shown in status.url when the ingress is not on 80/443 (k3d maps it to the host).</summary>
    [Range(1, 65535)]
    public int? PublicPort { get; set; }

    [Range(typeof(TimeSpan), "00:10:00", "30.00:00:00")]
    public TimeSpan MaxTtl { get; set; } = TimeSpan.FromHours(72);

    /// <summary>Image holding helm and the sample-api chart, released together with the operator.</summary>
    [Required]
    public string DeployerImage { get; set; } = string.Empty;

    /// <summary>Image of the data seeder; required for previews with <c>seedData: true</c>.</summary>
    public string? SeederImage { get; set; }

    [Required]
    public string PostgresImage { get; set; } =
        "postgres:17-alpine@sha256:266d3c257594e195b309ae9d1805e54c788f1d7b375f56bf6c367aac6c917e7b";

    [RegularExpression("^(Always|IfNotPresent|Never)$")]
    public string ImagePullPolicy { get; set; } = "IfNotPresent";

    [Required]
    public string IngressControllerNamespace { get; set; } = "kube-system";

    /// <summary>OTLP endpoint injected in the previews. Empty disables telemetry export.</summary>
    public string CollectorEndpoint { get; set; } = string.Empty;

    public string CollectorNamespace { get; set; } = "observability";

    /// <summary>ClusterRole granted to the deployer job inside each preview namespace.</summary>
    [Required]
    public string DeployerClusterRole { get; set; } = "preview-deployer";

    [Range(typeof(TimeSpan), "00:00:30", "01:00:00")]
    public TimeSpan ResyncPeriod { get; set; } = TimeSpan.FromMinutes(5);

    [Range(1, 16)]
    public int Workers { get; set; } = 2;

    /// <summary>Namespace the operator runs in: holds its leader lease.</summary>
    [Required]
    public string Namespace { get; set; } = "preview-system";

    /// <summary>The operator's ServiceAccount, granted access to each preview namespace it creates.</summary>
    [Required]
    public string ServiceAccount { get; set; } = "preview-operator";

    /// <summary>ClusterRole the operator binds to itself inside each preview namespace.</summary>
    [Required]
    public string NamespaceClusterRole { get; set; } = "preview-operator-namespace";

    [Required]
    public string LeaseName { get; set; } = "preview-operator";

    /// <summary>Namespace quota. Sized for the API, PostgreSQL and the deployment jobs of one preview.</summary>
    public string QuotaCpu { get; set; } = "2";

    public string QuotaMemory { get; set; } = "3Gi";
}
