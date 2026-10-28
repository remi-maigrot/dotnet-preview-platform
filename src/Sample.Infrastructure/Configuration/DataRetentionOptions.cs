using System.ComponentModel.DataAnnotations;

namespace Sample.Infrastructure.Configuration;

/// <summary>How long absence data is kept. The reasoning behind the defaults is in docs/data-protection.md.</summary>
public sealed class DataRetentionOptions : IValidatableObject
{
    public const string SectionName = "DataRetention";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Comments and rejection reasons are erased this long after the decision. They are only useful while the
    /// request is discussed, and they are where people write health details.
    /// </summary>
    [Range(typeof(TimeSpan), "1.00:00:00", "3650.00:00:00")]
    public TimeSpan FreeTextRetention { get; set; } = TimeSpan.FromDays(180);

    /// <summary>Requests are deleted this long after the absence ended (payroll and labour law records).</summary>
    [Range(typeof(TimeSpan), "30.00:00:00", "3650.00:00:00")]
    public TimeSpan RequestRetention { get; set; } = TimeSpan.FromDays(5 * 365);

    [Range(typeof(TimeSpan), "00:01:00", "7.00:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (FreeTextRetention >= RequestRetention)
        {
            yield return new ValidationResult(
                "DataRetention:FreeTextRetention must be shorter than DataRetention:RequestRetention.",
                [nameof(FreeTextRetention), nameof(RequestRetention)]);
        }
    }
}
