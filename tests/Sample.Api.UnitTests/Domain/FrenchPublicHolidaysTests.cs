using Sample.Api.UnitTests.TestSupport;
using Sample.Domain.Absences;

namespace Sample.Api.UnitTests.Domain;

public sealed class FrenchPublicHolidaysTests
{
    [Theory]
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    [InlineData(2038, 4, 25)]
    public void EasterSunday_MatchesKnownDates(int year, int month, int day) =>
        FrenchPublicHolidays.EasterSunday(year).ShouldBe(new DateOnly(year, month, day));

    [Fact]
    public void For2026_ContainsElevenHolidaysIncludingMovableFeasts()
    {
        var holidays = FrenchPublicHolidays.For(2026);

        holidays.Count.ShouldBe(11);
        holidays.ShouldContain(new DateOnly(2026, 4, 6));   // Easter Monday
        holidays.ShouldContain(new DateOnly(2026, 5, 14));  // Ascension Day
        holidays.ShouldContain(new DateOnly(2026, 5, 25));  // Whit Monday
        holidays.ShouldContain(new DateOnly(2026, 7, 14));
    }

    [Fact]
    public void Between_PeriodAcrossNewYear_CoversBothYears()
    {
        var holidays = FrenchPublicHolidays.Between(Build.Range("2026-12-20", "2027-01-05"));

        holidays.ShouldContain(new DateOnly(2026, 12, 25));
        holidays.ShouldContain(new DateOnly(2027, 1, 1));
    }

    [Fact]
    public void For_YearBeforeGregorianCalendar_Throws() =>
        Should.Throw<ArgumentOutOfRangeException>(() => FrenchPublicHolidays.For(1500));
}
