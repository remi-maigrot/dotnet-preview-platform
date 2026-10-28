
namespace Preview.DataSeeder.Tests;

public sealed class SyntheticDatasetTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Generate_IsDeterministic()
    {
        var first = SyntheticDataset.Generate(Now);
        var second = SyntheticDataset.Generate(Now);

        second.Employees.ShouldBe(first.Employees);
        second.AbsenceRequests.ShouldBe(first.AbsenceRequests);
    }

    [Fact]
    public void Generate_ContainsTheKnownAccounts()
    {
        var dataset = SyntheticDataset.Generate(Now);

        dataset.Employees.ShouldContain(e => e.Id == KnownAccounts.Manager && e.ManagerId == null);
        dataset.Employees.ShouldContain(e => e.Id == KnownAccounts.Employee && e.ManagerId == KnownAccounts.Manager);
    }

    [Fact]
    public void Generate_ProducesConsistentNonOverlappingHistory()
    {
        var dataset = SyntheticDataset.Generate(Now);
        var managers = dataset.Employees.ToDictionary(e => e.Id, e => e.ManagerId);

        dataset.AbsenceRequests.Count.ShouldBeGreaterThan(40);
        dataset.Employees.ShouldAllBe(e => e.Email.EndsWith("@example.invalid", StringComparison.Ordinal));
        dataset.Employees.Select(e => e.Email).ShouldBeUnique();
        dataset.AbsenceRequests.ShouldAllBe(r => managers.ContainsKey(r.EmployeeId) && r.EndDate >= r.StartDate && r.WorkingDays > 0);
        dataset.AbsenceRequests.Where(r => r.Status is "Approved" or "Rejected")
            .ShouldAllBe(r => r.DecidedBy == managers[r.EmployeeId]);

        foreach (var requests in dataset.AbsenceRequests.GroupBy(r => r.EmployeeId))
        {
            var ordered = requests.OrderBy(r => r.StartDate).ToList();
            ordered.Zip(ordered.Skip(1)).ShouldAllBe(pair => pair.First.EndDate < pair.Second.StartDate);
        }
    }

    [Fact]
    public void Generate_LeavesTheSmokeEmployeeWithoutHistory() =>
        SyntheticDataset.Generate(Now).AbsenceRequests.ShouldNotContain(r => r.EmployeeId == KnownAccounts.Employee);
}
