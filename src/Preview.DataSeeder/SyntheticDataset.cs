using Bogus;
using Sample.Domain.Absences;

namespace Preview.DataSeeder;

/// <summary>
/// A realistic company of fictitious people: managers with teams, a few months of history and some upcoming leave.
/// The Bogus seed is fixed, so every preview gets the same data and test failures are reproducible.
/// </summary>
internal static class SyntheticDataset
{
    public const int Seed = 4242;
    private const int Managers = 4;
    private const int TeamSize = 6;

    private static readonly string[] Types = ["PaidLeave", "PaidLeave", "PaidLeave", "CompensatoryTime", "SickLeave", "UnpaidLeave"];

    public static Dataset Generate(DateTimeOffset now)
    {
        var faker = new Faker("fr") { Random = new Randomizer(Seed) };
        var employees = new List<EmployeeRow>
        {
            new(KnownAccounts.Manager, KnownAccounts.ManagerName, "preview.manager@example.invalid", null),
            new(KnownAccounts.Employee, KnownAccounts.EmployeeName, "preview.employee@example.invalid", KnownAccounts.Manager),
        };

        for (var m = 0; m < Managers; m++)
        {
            var manager = Person(faker, null);
            employees.Add(manager);
            for (var e = 0; e < TeamSize; e++)
            {
                employees.Add(Person(faker, manager.Id));
            }
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var requests = employees
            .Where(e => e.ManagerId is not null && e.Id != KnownAccounts.Employee)
            .SelectMany(e => History(faker, e, today, now))
            .ToList();
        return new Dataset(employees, requests);
    }

    private static EmployeeRow Person(Faker faker, Guid? managerId)
    {
        var first = faker.Name.FirstName();
        var last = faker.Name.LastName();
        var id = faker.Random.Guid();
        return new EmployeeRow(id, $"{first} {last}", $"{faker.Internet.UserName(first, last).ToLowerInvariant()}.{id.ToString("N")[..6]}@example.invalid", managerId);
    }

    /// <summary>Non-overlapping requests from about four months ago to two months ahead.</summary>
    private static IEnumerable<AbsenceRequestRow> History(Faker faker, EmployeeRow employee, DateOnly today, DateTimeOffset now)
    {
        var cursor = today.AddDays(-faker.Random.Int(100, 130));
        var count = faker.Random.Int(2, 5);
        for (var i = 0; i < count; i++)
        {
            cursor = cursor.AddDays(faker.Random.Int(7, 40));
            var length = faker.Random.Int(0, 9);
            var period = DateRange.Create(cursor, cursor.AddDays(length)).Value;
            cursor = period.End;

            var workingDays = WorkingDays.Count(period, FrenchPublicHolidays.Between(period));
            if (workingDays == 0)
            {
                continue;
            }

            var submitted = new DateTimeOffset(period.Start.AddDays(-faker.Random.Int(5, 30)).ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(9))), TimeSpan.Zero);
            var (status, decidedAt, decidedBy, reason) = Decide(faker, employee, period, submitted, today);
            yield return new AbsenceRequestRow(
                faker.Random.Guid(),
                employee.Id,
                faker.PickRandom(Types),
                status,
                period.Start,
                period.End,
                workingDays,
                faker.Random.Bool(0.4f) ? faker.Lorem.Sentence(6) : null,
                submitted < now ? submitted : now.AddDays(-1),
                decidedAt,
                decidedBy,
                reason);
        }
    }

    private static (string Status, DateTimeOffset? DecidedAt, Guid? DecidedBy, string? Reason) Decide(
        Faker faker, EmployeeRow employee, DateRange period, DateTimeOffset submitted, DateOnly today)
    {
        var decidedAt = submitted.AddDays(faker.Random.Int(1, 4));
        if (period.Start > today && faker.Random.Bool(0.5f))
        {
            return ("Submitted", null, null, null);
        }

        return faker.Random.Int(0, 9) switch
        {
            0 => ("Rejected", decidedAt, employee.ManagerId, "Team coverage is too low that week."),
            1 => ("Cancelled", decidedAt, null, null),
            _ => ("Approved", decidedAt, employee.ManagerId, null),
        };
    }
}
