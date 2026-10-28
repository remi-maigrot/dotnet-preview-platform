using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sample.Application;
using Sample.Infrastructure;
using Sample.Infrastructure.Persistence;
using Sample.Migrator;

// Usage: Sample.Migrator [apply|list]
//   apply (default)  applies pending migrations, exits non-zero on failure
//   list             prints applied and pending migrations without changing anything
var command = args.FirstOrDefault() ?? "apply";
if (command is not ("apply" or "list"))
{
    await Console.Error.WriteLineAsync($"Unknown command '{command}'. Expected 'apply' or 'list'.");
    return 2;
}

var builder = Host.CreateApplicationBuilder(args.Skip(1).ToArray());
builder.Configuration.AddKeyPerFile(builder.Configuration["Secrets:Path"] ?? "/var/run/secrets/sample-migrator", optional: true);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.UseUtcTimestamp = true);
builder.Services.AddApplication(builder.Configuration).AddInfrastructure(builder.Configuration);

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Sample.Migrator");
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));

try
{
    await using var scope = host.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // On an empty database EF Core queries the history table before creating it and logs that as an error.
    // Creating it up front keeps the job logs meaningful: an error line always means a real failure.
    var history = db.GetService<IHistoryRepository>();
    if (command == "apply")
    {
        await history.CreateIfNotExistsAsync(timeout.Token);
    }

    var applied = await history.ExistsAsync(timeout.Token)
        ? (await db.Database.GetAppliedMigrationsAsync(timeout.Token)).ToList()
        : [];
    var pending = db.Database.GetMigrations().Except(applied).ToList();
    logger.MigrationState(applied.Count, pending.Count, applied.LastOrDefault() ?? "none");

    if (command == "list")
    {
        foreach (var migration in applied)
        {
            Console.WriteLine($"applied  {migration}");
        }

        foreach (var migration in pending)
        {
            Console.WriteLine($"pending  {migration}");
        }

        return 0;
    }

    // EF Core takes a database-level lock while migrating, so two jobs started by mistake cannot interleave.
    await db.Database.MigrateAsync(timeout.Token);
    logger.MigrationsApplied(pending.Count);
    return 0;
}
catch (Exception ex)
{
    logger.MigrationFailed(ex);
    return 1;
}
