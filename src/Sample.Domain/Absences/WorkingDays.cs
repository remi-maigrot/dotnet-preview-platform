namespace Sample.Domain.Absences;

/// <summary>Counts the working days of a period: weekends and public holidays are not deducted.</summary>
public static class WorkingDays
{
    public static int Count(DateRange period, IReadOnlySet<DateOnly> publicHolidays)
    {
        ArgumentNullException.ThrowIfNull(publicHolidays);

        return period.Days().Count(day =>
            day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !publicHolidays.Contains(day));
    }
}
