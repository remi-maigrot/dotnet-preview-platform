using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sample.Infrastructure.Configuration;

namespace Sample.Infrastructure.Retention;

/// <summary>Runs <see cref="DataRetentionPurger"/> at startup, then every <see cref="DataRetentionOptions.Interval"/>.</summary>
internal sealed partial class DataRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<DataRetentionOptions> options,
    TimeProvider clock,
    ILogger<DataRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            LogDisabled();
            return;
        }

        using var timer = new PeriodicTimer(options.Value.Interval, clock);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<DataRetentionPurger>().RunAsync(clock.GetUtcNow(), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Retried on the next tick; a database hiccup must not stop the API.
                LogFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(2101, LogLevel.Warning, "Data retention is disabled (DataRetention:Enabled=false): personal data is kept indefinitely")]
    private partial void LogDisabled();

    [LoggerMessage(2102, LogLevel.Error, "Data retention run failed")]
    private partial void LogFailed(Exception exception);
}
