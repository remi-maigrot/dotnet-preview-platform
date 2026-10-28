using System.Net.Http.Headers;
using Sample.Domain.Employees;

namespace Sample.Api.IntegrationTests.Infrastructure;

/// <summary>A manager with two direct reports, plus an outsider from another team. Fresh ids for every test.</summary>
internal sealed record Team(Employee Manager, Employee Alice, Employee Bob, Employee Outsider)
{
    public static async Task<Team> CreateAsync(PostgresFixture postgres)
    {
        var manager = new Employee(Guid.NewGuid(), "Manager", Email("manager"), null);
        var team = new Team(
            manager,
            new Employee(Guid.NewGuid(), "Alice", Email("alice"), manager.Id),
            new Employee(Guid.NewGuid(), "Bob", Email("bob"), manager.Id),
            new Employee(Guid.NewGuid(), "Outsider", Email("outsider"), null));

        await using var db = postgres.CreateDbContext();
        db.Employees.AddRange(team.Manager, team.Alice, team.Bob, team.Outsider);
        await db.SaveChangesAsync();
        return team;
    }

    public static HttpClient As(HttpClient client, Employee employee, bool manager = false)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.For(employee.Id, manager));
        return client;
    }

    private static string Email(string name) => $"{name}-{Guid.NewGuid():N}@example.test";
}
