using System.ComponentModel.DataAnnotations;

namespace Sample.Infrastructure.Configuration;

public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    /// <summary>How long a stored response can be replayed.</summary>
    [Range(typeof(TimeSpan), "00:05:00", "7.00:00:00")]
    public TimeSpan Retention { get; set; } = TimeSpan.FromHours(24);

    [Range(typeof(TimeSpan), "00:00:10", "1.00:00:00")]
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromHours(1);
}
