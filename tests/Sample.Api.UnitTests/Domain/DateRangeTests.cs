using Sample.Api.UnitTests.TestSupport;
using Sample.Domain.Absences;

namespace Sample.Api.UnitTests.Domain;

public sealed class DateRangeTests
{
    [Fact]
    public void Create_EndBeforeStart_Fails()
    {
        var result = DateRange.Create(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 9));

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(AbsenceErrors.EndBeforeStart);
    }

    [Fact]
    public void Create_SingleDay_HasOneCalendarDay()
    {
        var range = Build.Range("2026-03-10", "2026-03-10");

        range.CalendarDays.ShouldBe(1);
        range.Days().ShouldHaveSingleItem().ShouldBe(new DateOnly(2026, 3, 10));
    }

    [Theory]
    [InlineData("2026-03-01", "2026-03-05", "2026-03-05", "2026-03-08", true)]   // touching on the last day
    [InlineData("2026-03-01", "2026-03-10", "2026-03-03", "2026-03-04", true)]   // contained
    [InlineData("2026-03-01", "2026-03-05", "2026-03-06", "2026-03-08", false)]  // adjacent
    [InlineData("2026-03-06", "2026-03-08", "2026-03-01", "2026-03-05", false)]  // before
    public void Overlaps_IsSymmetric(string aStart, string aEnd, string bStart, string bEnd, bool expected)
    {
        var a = Build.Range(aStart, aEnd);
        var b = Build.Range(bStart, bEnd);

        a.Overlaps(b).ShouldBe(expected);
        b.Overlaps(a).ShouldBe(expected);
    }

    [Fact]
    public void Value_OfFailedResult_Throws()
    {
        var result = DateRange.Create(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 9));

        Should.Throw<InvalidOperationException>(() => result.Value).Message.ShouldContain(AbsenceErrors.EndBeforeStart.Code);
    }
}
