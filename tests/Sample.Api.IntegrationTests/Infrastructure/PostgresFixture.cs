using Microsoft.EntityFrameworkCore;
using Sample.Api.IntegrationTests.Infrastructure;
using Sample.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(PostgresFixture))]

namespace Sample.Api.IntegrationTests.Infrastructure;

/// <summary>
/// One PostgreSQL container for the whole test assembly, migrated once. Tests stay independent by creating their
/// own employees with fresh ids, so they can run in parallel without resetting the database.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public AppDbContext CreateDbContext() => CreateDbContext(ConnectionString);

    /// <summary>
    /// A migrated database of its own, in the same container, for tests that touch every row (retention purges)
    /// and would otherwise delete the data of tests running in parallel.
    /// </summary>
    public async Task<string> CreateIsolatedDatabaseAsync()
    {
        var name = $"isolated_{Guid.NewGuid():N}";
        await using (var db = CreateDbContext())
        {
            // CREATE DATABASE takes no parameters; the name is a generated GUID, not input.
            var createDatabase = "CREATE DATABASE " + name;
            await db.Database.ExecuteSqlRawAsync(createDatabase);
        }

        var connectionString = new Npgsql.NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
        await using (var db = CreateDbContext(connectionString))
        {
            await db.Database.MigrateAsync();
        }

        return connectionString;
    }

    public static AppDbContext CreateDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options);

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
