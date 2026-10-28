namespace Sample.Api.IntegrationTests.Infrastructure;

internal static class Dates
{
    /// <summary>The Monday <paramref name="weeksAhead"/> weeks from now, so submissions are never in the past.</summary>
    public static DateOnly Monday(int weeksAhead)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var daysToMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(daysToMonday + (7 * weeksAhead));
    }
}
