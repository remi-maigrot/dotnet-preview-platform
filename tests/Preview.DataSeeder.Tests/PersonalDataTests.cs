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

    // Fixed ids keep the fake identities stable from run to run. The surnames are absent from the "fr" locale
    // of Bogus, so finding one in the copy can only mean it was copied, not drawn by chance.
    internal static Dataset RealisticSource()
    {
        var manager = new EmployeeRow(Guid.Parse("0b6f1c52-3a9e-4d1f-9b7e-2f4c8a1d6e01"), "Camille Vauquelin", "camille.vauquelin@corp.test", null);
        var alice = new EmployeeRow(Guid.Parse("4e2a9d17-8c3b-4f6a-a5d2-7b1e9c3f0a02"), "Alice Arbogast", "alice.arbogast@corp.test", manager.Id);
        var bob = new EmployeeRow(Guid.Parse("9c7d3e81-2b5f-4a0c-8e6d-1a4f7b2c9d03"), "Bruno Kerjean", "bruno.kerjean@corp.test", manager.Id);
        var requests = new[]
        {
            Request(Guid.Parse("d1a2b3c4-0001-4000-8000-000000000001"), alice, "2026-03-09", "2026-03-13", "Approved", "Wedding of my sister, call me on 06 12 34 56 78", manager.Id, null),
            Request(Guid.Parse("d1a2b3c4-0002-4000-8000-000000000002"), bob, "2026-04-06", "2026-04-07", "Rejected", "Medical appointment at the hospital", manager.Id, "Bruno, the release is that week"),
        };
        return new Dataset([manager, alice, bob], requests);
    }

    private static AbsenceRequestRow Request(Guid id, EmployeeRow employee, string start, string end, string status, string comment, Guid decider, string? reason) =>
        new(id, employee.Id, "PaidLeave", status, DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture),
            DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture), 3, comment,
            new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 2, 2, 9, 0, 0, TimeSpan.Zero), decider, reason);
}
