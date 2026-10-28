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

    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
