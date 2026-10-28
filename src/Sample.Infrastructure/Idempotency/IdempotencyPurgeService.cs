using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sample.Application.Abstractions;
using Sample.Infrastructure.Configuration;

namespace Sample.Infrastructure.Idempotency;

/// <summary>Deletes expired idempotency entries. Safe to run on every replica: the delete is idempotent.</summary>
internal sealed partial class IdempotencyPurgeService(
    IServiceScopeFactory scopeFactory,
    IOptions<IdempotencyOptions> options,
    TimeProvider clock,
    ILogger<IdempotencyPurgeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PurgeInterval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
                var purged = await store.PurgeExpiredAsync(clock.GetUtcNow() - options.Value.Retention, stoppingToken);
                LogPurged(purged);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failed purge is retried on the next tick; it must not stop the host.
                LogPurgeFailed(ex);
            }
        }
    }

    [LoggerMessage(2000, LogLevel.Debug, "Purged {Count} expired idempotency entries")]
    private partial void LogPurged(int count);

    [LoggerMessage(2001, LogLevel.Warning, "Idempotency purge failed")]
    private partial void LogPurgeFailed(Exception exception);
}
