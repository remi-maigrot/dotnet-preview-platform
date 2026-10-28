using System.ComponentModel.DataAnnotations;

namespace Sample.Infrastructure.Configuration;

/// <summary>
/// External public holiday calendar (any API exposing <c>GET {base}/PublicHolidays/{year}/{country}</c>).
/// Only used when the <c>Features:ExternalHolidayCalendar</c> flag is on.
/// </summary>
public sealed class HolidayCalendarOptions
{
    public const string SectionName = "HolidayCalendar";

    [Url]
    public string? BaseAddress { get; set; }

    [Required]
    [RegularExpression("^[A-Z]{2}$", ErrorMessage = "HolidayCalendar:CountryCode must be an ISO 3166-1 alpha-2 code.")]
    public string CountryCode { get; set; } = "FR";

    /// <summary>Timeout of a single HTTP attempt.</summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:00:30")]
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Upper bound for the call, retries included.</summary>
    [Range(typeof(TimeSpan), "00:00:00.500", "00:01:00")]
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(8);

    [Range(0, 5)]
    public int MaxRetryAttempts { get; set; } = 3;
}
