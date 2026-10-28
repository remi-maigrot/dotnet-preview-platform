using k8s;
using k8s.LeaderElection;
using k8s.LeaderElection.ResourceLock;
using Microsoft.Extensions.Options;
using Preview.Operator.Diagnostics;

namespace Preview.Operator.Controller;

/// <summary>
/// Runs the controller on one replica at a time, using a coordination.k8s.io Lease. The others stay hot standbys.
/// Losing the lease stops the process: a restart is the simplest way to guarantee no stale worker keeps writing.
/// </summary>
internal sealed class LeaderElectedService(
    IKubernetes client,
    PreviewController controller,
    OperatorMetrics metrics,
    IOptions<OperatorOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<LeaderElectedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var identity = Environment.GetEnvironmentVariable("POD_NAME") ?? Environment.MachineName;
        var leaseLock = new LeaseLock(client, options.Value.Namespace, options.Value.LeaseName, identity);
        using var elector = new LeaderElector(new LeaderElectionConfig(leaseLock)
        {
            LeaseDuration = TimeSpan.FromSeconds(15),
            RenewDeadline = TimeSpan.FromSeconds(10),
            RetryPeriod = TimeSpan.FromSeconds(2),
        });

        using var leading = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        Task? running = null;
        elector.OnStartedLeading += () =>
        {
            logger.StartedLeading(identity);
            metrics.SetLeader(true);
            running = Task.Run(() => controller.RunAsync(leading.Token), leading.Token);
        };
        elector.OnStoppedLeading += () =>
        {
            logger.StoppedLeading(identity);
            metrics.SetLeader(false);
            leading.Cancel();
            lifetime.StopApplication();
        };
        elector.OnNewLeader += leader => logger.NewLeader(leader);

        await elector.RunUntilLeadershipLostAsync(stoppingToken);
        if (running is not null)
        {
            try
            {
                await running;
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown or leadership loss.
            }
        }
    }
}
