using Sample.Domain.Absences;
using Sample.Domain.Employees;

namespace Sample.Api.UnitTests.TestSupport;

/// <summary>Test data builders. Dates are fixed so tests never depend on the day they run.</summary>
internal static class Build
{
    /// <summary>Wednesday 2026-03-04, 09:00 UTC.</summary>
    public static readonly DateTimeOffset Now = new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);

    public static readonly IReadOnlySet<DateOnly> NoHolidays = new HashSet<DateOnly>();

    public static Employee Manager() => new(Guid.NewGuid(), "Manager", $"manager-{Guid.NewGuid():N}@example.test", null);

    public static Employee EmployeeOf(Employee manager) =>
        new(Guid.NewGuid(), "Employee", $"employee-{Guid.NewGuid():N}@example.test", manager.Id);

    public static DateRange Range(string start, string end) =>
        DateRange.Create(DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture)).Value;

    /// <summary>A pending request for Monday 2026-03-09 to Friday 2026-03-13.</summary>
    public static AbsenceRequest PendingRequest(Employee employee, AbsenceType type = AbsenceType.PaidLeave) =>
        AbsenceRequest.Submit(
            Guid.NewGuid(),
            employee,
            type,
            Range("2026-03-09", "2026-03-13"),
            comment: null,
            NoHolidays,
            [],
            Now).Value;
}
