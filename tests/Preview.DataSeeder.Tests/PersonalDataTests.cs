using System.Reflection;
using System.Text.Json;
using Sample.Domain.Absences;
using Sample.Domain.Common;
using Sample.Domain.Employees;

namespace Preview.DataSeeder.Tests;

/// <summary>
/// The guarantee behind preview data: nothing marked [PersonalData] in the domain leaves pseudonymisation in clear.
/// Adding a [PersonalData] property without handling it in the seeder makes these tests fail.
/// </summary>
public sealed class PersonalDataTests
{
    private static readonly byte[] Salt = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private static readonly Dictionary<Type, Type> RowOf = new()
    {
        [typeof(Employee)] = typeof(EmployeeRow),
        [typeof(AbsenceRequest)] = typeof(AbsenceRequestRow),
    };

    public static TheoryData<string, string> PersonalProperties()
    {
        var data = new TheoryData<string, string>();
        foreach (var property in typeof(Employee).Assembly.GetTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.GetCustomAttribute<PersonalDataAttribute>() is not null))
        {
            data.Add(property.DeclaringType!.Name, property.Name);
        }

        return data;
    }

    [Fact]
    public void Domain_DeclaresPersonalData() => PersonalProperties().Count.ShouldBeGreaterThanOrEqualTo(4);

    [Theory]
    [MemberData(nameof(PersonalProperties))]
    public void PersonalProperty_IsNeverCopiedInClear(string entity, string property)
    {
        var domainType = typeof(Employee).Assembly.GetTypes().Single(t => t.Name == entity);
        RowOf.ShouldContainKey(domainType, $"{entity} holds personal data but the seeder does not copy it: add a row type.");
        var column = RowOf[domainType].GetProperty(property);
        column.ShouldNotBeNull($"{entity}.{property} is personal data but has no column in {RowOf[domainType].Name}.");

        var source = RealisticSource();
        var copy = new Pseudonymizer(Salt).Pseudonymize(source);

        var original = Values(source, domainType, column).ToHashSet();
        var copied = Values(copy, domainType, column).ToList();
        copied.ShouldNotBeEmpty();
        copied.ShouldAllBe(value => !original.Contains(value), $"{entity}.{property} leaked a real value.");
    }

    [Fact]
    public void RealNames_AppearNowhereInTheCopy()
    {
        var source = RealisticSource();

        var json = JsonSerializer.Serialize(new Pseudonymizer(Salt).Pseudonymize(source));

        foreach (var person in source.Employees)
        {
            json.ShouldNotContain(person.FullName.Split(' ')[1]);
            json.ShouldNotContain(person.Email);
        }
    }

    private static IEnumerable<string> Values(Dataset dataset, Type domainType, PropertyInfo column)
    {
        IEnumerable<object> rows = domainType == typeof(Employee) ? dataset.Employees : dataset.AbsenceRequests;
        return rows.Select(r => column.GetValue(r)?.ToString()).OfType<string>();
    }

    internal static Dataset RealisticSource()
    {
        var manager = new EmployeeRow(Guid.NewGuid(), "Camille Lefebvre", "camille.lefebvre@corp.test", null);
        var alice = new EmployeeRow(Guid.NewGuid(), "Alice Moreau", "alice.moreau@corp.test", manager.Id);
        var bob = new EmployeeRow(Guid.NewGuid(), "Bruno Garnier", "bruno.garnier@corp.test", manager.Id);
        var requests = new[]
        {
            Request(alice, "2026-03-09", "2026-03-13", "Approved", "Wedding of my sister, call me on 06 12 34 56 78", manager.Id, null),
            Request(bob, "2026-04-06", "2026-04-07", "Rejected", "Medical appointment at the hospital", manager.Id, "Bruno, the release is that week"),
        };
        return new Dataset([manager, alice, bob], requests);
    }

    private static AbsenceRequestRow Request(EmployeeRow employee, string start, string end, string status, string comment, Guid decider, string? reason) =>
        new(Guid.NewGuid(), employee.Id, "PaidLeave", status, DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture),
            DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture), 3, comment,
            new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 2, 2, 9, 0, 0, TimeSpan.Zero), decider, reason);
}
