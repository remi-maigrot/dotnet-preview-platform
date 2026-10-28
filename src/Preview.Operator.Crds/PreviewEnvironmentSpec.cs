using System.Text.Json.Serialization;

namespace Preview.Operator.Crds;

public sealed class PreviewEnvironmentSpec
{
    public const int LocalEnvironment = 0;

    /// <summary>
    /// Pull request number. Drives the namespace (<c>pr-42</c>) and the host (<c>pr-42.preview.localtest.me</c>).
    /// <see cref="LocalEnvironment"/> (0) is the developer's working copy deployed by <c>devctl up</c>.
    /// </summary>
    [JsonPropertyName("pullRequest")]
    public int PullRequest { get; set; }

    /// <summary>API image, tagged with the commit SHA (<c>ghcr.io/org/sample-api:3f2c1a…</c>) or pinned by digest.</summary>
    [JsonPropertyName("image")]
    public string Image { get; set; } = string.Empty;

    /// <summary>Lifetime counted from creation, as a Go-style duration (<c>2h</c>, <c>90m</c>, <c>1h30m</c>).</summary>
    [JsonPropertyName("ttl")]
    public string Ttl { get; set; } = "4h";

    [JsonPropertyName("resources")]
    public PreviewResources? Resources { get; set; }

    /// <summary>Populate the database with pseudonymised, realistic data once the schema is migrated.</summary>
    [JsonPropertyName("seedData")]
    public bool SeedData { get; set; }

    /// <summary>Optional metadata recorded on the release (Grafana annotation, labels).</summary>
    [JsonPropertyName("commitSha")]
    public string? CommitSha { get; set; }
}

/// <summary>Size of the environment. The namespace quota is derived from it with headroom for the database and jobs.</summary>
public sealed class PreviewResources
{
    [JsonPropertyName("cpu")]
    public string Cpu { get; set; } = "500m";

    [JsonPropertyName("memory")]
    public string Memory { get; set; } = "256Mi";
}
