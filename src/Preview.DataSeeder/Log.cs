using Microsoft.Extensions.Logging;

namespace Preview.DataSeeder;

internal static partial class Log
{
    [LoggerMessage(1, LogLevel.Information, "Database already seeded, nothing to do")]
    public static partial void AlreadySeeded(this ILogger logger);

    [LoggerMessage(2, LogLevel.Information, "Seeded {Mode} data: {Employees} employees, {Requests} absence requests")]
    public static partial void Seeded(this ILogger logger, string mode, int employees, int requests);

    [LoggerMessage(3, LogLevel.Critical, "Seeding failed")]
    public static partial void SeedFailed(this ILogger logger, Exception exception);
}
