using System.ComponentModel.DataAnnotations;

namespace Sample.Api.Configuration;

/// <summary>Token bucket per authenticated user (or per client IP for anonymous calls).</summary>
internal sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, 100_000)]
    public int PermitLimit { get; set; } = 100;

    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(10);
}
