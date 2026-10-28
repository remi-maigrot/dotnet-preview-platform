using Sample.Domain.Common;

namespace Sample.Domain.Absences;

/// <summary>An inclusive range of calendar days.</summary>
public readonly record struct DateRange
{
    private DateRange(DateOnly start, DateOnly end)
    {
        Start = start;
        End = end;
    }

    public DateOnly Start { get; }

    public DateOnly End { get; }

    public int CalendarDays => End.DayNumber - Start.DayNumber + 1;

    public static Result<DateRange> Create(DateOnly start, DateOnly end) =>
        end < start ? AbsenceErrors.EndBeforeStart : new DateRange(start, end);

    public bool Overlaps(DateRange other) => Start <= other.End && other.Start <= End;

    public IEnumerable<DateOnly> Days()
    {
        for (var day = Start; day <= End; day = day.AddDays(1))
        {
            yield return day;
        }
    }
}
