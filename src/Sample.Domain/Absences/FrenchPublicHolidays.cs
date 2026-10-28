namespace Sample.Domain.Absences;

/// <summary>
/// The eleven French public holidays, computed locally. Easter-based holidays use the anonymous Gregorian
/// algorithm (Meeus/Jones/Butcher), so no network call is needed.
/// </summary>
public static class FrenchPublicHolidays
{
    public static IReadOnlySet<DateOnly> For(int year)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 1583);

        var easterSunday = EasterSunday(year);
        return new HashSet<DateOnly>
        {
            new(year, 1, 1),
            easterSunday.AddDays(1),   // Easter Monday
            new(year, 5, 1),
            new(year, 5, 8),
            easterSunday.AddDays(39),  // Ascension Day
            easterSunday.AddDays(50),  // Whit Monday
            new(year, 7, 14),
            new(year, 8, 15),
            new(year, 11, 1),
            new(year, 11, 11),
            new(year, 12, 25),
        };
    }

    public static IReadOnlySet<DateOnly> Between(DateRange period)
    {
        var holidays = new HashSet<DateOnly>();
        for (var year = period.Start.Year; year <= period.End.Year; year++)
        {
            holidays.UnionWith(For(year));
        }

        return holidays;
    }

    internal static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}
