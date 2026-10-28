using Sample.Api.UnitTests.TestSupport;
using Sample.Domain.Absences;

namespace Sample.Api.UnitTests.Domain;

public sealed class WorkingDaysTests
{
    [Fact]
    public void Count_FullWeek_ExcludesWeekend() =>
        WorkingDays.Count(Build.Range("2026-03-09", "2026-03-15"), Build.NoHolidays).ShouldBe(5);

    [Fact]
    public void Count_WeekWithPublicHoliday_ExcludesHoliday()
    {
        var holidays = new HashSet<DateOnly> { new(2026, 3, 11) };

        WorkingDays.Count(Build.Range("2026-03-09", "2026-03-13"), holidays).ShouldBe(4);
    }

    [Fact]
    public void Count_WeekendOnly_IsZero() =>
        WorkingDays.Count(Build.Range("2026-03-14", "2026-03-15"), Build.NoHolidays).ShouldBe(0);
}
