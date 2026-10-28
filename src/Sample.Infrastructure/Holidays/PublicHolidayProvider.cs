using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Sample.Application.Abstractions;
using Sample.Application.Configuration;
using Sample.Domain.Absences;

namespace Sample.Infrastructure.Holidays;

/// <summary>
/// Uses the external calendar when the feature flag is on and degrades to the built-in calendar when it is
/// unavailable: a holiday lookup must never block an absence request.
/// </summary>
internal sealed partial class PublicHolidayProvider(
    IOptionsMonitor<FeatureFlagsOptions> features,
    HolidayCalendarClient external,
    BuiltInHolidayProvider builtIn,
    ILogger<PublicHolidayProvider> logger) : IPublicHolidayProvider
{
    public async Task<IReadOnlySet<DateOnly>> GetHolidaysAsync(DateRange period, CancellationToken cancellationToken)
    {
        if (!features.CurrentValue.ExternalHolidayCalendar)
        {
            return await builtIn.GetHolidaysAsync(period, cancellationToken);
        }

        try
        {
            var holidays = new HashSet<DateOnly>();
            for (var year = period.Start.Year; year <= period.End.Year; year++)
            {
                holidays.UnionWith(await external.GetHolidaysAsync(year, cancellationToken));
            }

            return holidays;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException
            or System.Text.Json.JsonException)
        {
            LogFallback(ex);
            return await builtIn.GetHolidaysAsync(period, cancellationToken);
        }
    }

    [LoggerMessage(3000, LogLevel.Warning, "External holiday calendar unavailable, falling back to the built-in calendar")]
    private partial void LogFallback(Exception exception);
}
