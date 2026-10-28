using Microsoft.Extensions.Logging;

namespace Sample.Migrator;

internal static partial class Log
{
    [LoggerMessage(1, LogLevel.Information, "{Applied} migrations applied, {Pending} pending (latest applied: {Latest})")]
    public static partial void MigrationState(this ILogger logger, int applied, int pending, string latest);

    [LoggerMessage(2, LogLevel.Information, "Database schema is up to date ({Count} migrations applied in this run)")]
    public static partial void MigrationsApplied(this ILogger logger, int count);

    [LoggerMessage(3, LogLevel.Critical, "Migration failed; the release must not proceed")]
    public static partial void MigrationFailed(this ILogger logger, Exception exception);
}
