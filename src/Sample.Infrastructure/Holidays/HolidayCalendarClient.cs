using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Sample.Infrastructure.Configuration;

namespace Sample.Infrastructure.Holidays;

/// <summary>Typed client for the external calendar. Retries, timeouts and circuit breaking live in its HTTP pipeline.</summary>
internal sealed class HolidayCalendarClient(HttpClient http, IOptions<HolidayCalendarOptions> options)
{
    public async Task<IReadOnlySet<DateOnly>> GetHolidaysAsync(int year, CancellationToken cancellationToken)
    {
        var country = options.Value.CountryCode;
        var holidays = await http.GetFromJsonAsync<List<HolidayDto>>($"PublicHolidays/{year}/{country}", cancellationToken)
            ?? [];
        return holidays.Select(h => h.Date).ToHashSet();
    }

    private sealed record HolidayDto(DateOnly Date);
}
