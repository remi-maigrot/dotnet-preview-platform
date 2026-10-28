using System.Diagnostics;
using k8s;
using Microsoft.Extensions.Options;
using Preview.Operator.Crds;
using Preview.Operator.Diagnostics;
using Preview.Operator.Reconciliation;

namespace Preview.Operator.Controller;

/// <summary>
/// List-watch loop feeding the work queue, plus the workers draining it. Only runs while this replica is leader.
/// Events are treated as hints: the reconciler always reads the current object before acting.
/// </summary>
internal sealed class PreviewController(
    IKubernetes client,
    IClusterGateway cluster,
    IServiceScopeFactory scopes,
    OperatorMetrics metrics,
    IOptions<OperatorOptions> options,
    TimeProvider clock,
    ILogger<PreviewController> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var queue = new WorkQueue(clock, baseDelay: TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromMinutes(5));
        var workers = Enumerable.Range(0, options.Value.Workers)
            .Select(_ => WorkAsync(queue, cancellationToken))
            .ToList();

        await Task.WhenAll([WatchAsync(queue, cancellationToken), ResyncAsync(queue, cancellationToken), .. workers]);
    }

    private async Task WatchAsync(WorkQueue queue, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // List, enqueue everything, then watch from the list's resourceVersion so no change is missed.
                var list = await client.CustomObjects.ListClusterCustomObjectAsync<PreviewEnvironmentList>(
                    PreviewEnvironment.Group, PreviewEnvironment.Version, PreviewEnvironment.Plural,
                    cancellationToken: cancellationToken);
                foreach (var item in list.Items)
                {
                    queue.Add(item.Metadata.Name);
                }

                var events = client.CustomObjects.WatchListClusterCustomObjectAsync(
                    PreviewEnvironment.Group, PreviewEnvironment.Version, PreviewEnvironment.Plural,
                    allowWatchBookmarks: true, resourceVersion: list.Metadata.ResourceVersion, timeoutSeconds: 300,
                    cancellationToken: cancellationToken);

                await foreach (var (type, payload) in events)
                {
                    if (type is WatchEventType.Bookmark or WatchEventType.Error)
                    {
                        continue;
                    }

                    var item = KubernetesJson.Deserialize<PreviewEnvironment>(KubernetesJson.Serialize(payload));
                    queue.Add(item.Metadata.Name);
                    metrics.SetQueueDepth(queue.Count);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 410 Gone, network blips, API server restarts: start over from a fresh list after a short pause.
                logger.WatchRestarted(ex);
                await Task.Delay(TimeSpan.FromSeconds(2), clock, cancellationToken);
            }
        }
    }

    private async Task ResyncAsync(WorkQueue queue, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(options.Value.ResyncPeriod, clock);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            foreach (var preview in await cluster.ListPreviewsAsync(cancellationToken))
            {
                queue.Add(preview.Metadata.Name);
            }
        }
    }

    private async Task WorkAsync(WorkQueue queue, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var name = await queue.TakeAsync(cancellationToken);
            var started = Stopwatch.GetTimestamp();
            try
            {
                var preview = await cluster.GetPreviewAsync(name, cancellationToken);
                if (preview is null)
                {
                    metrics.Forget(name);
                    queue.Forget(name);
                    continue;
                }

                await using var scope = scopes.CreateAsyncScope();
                var reconciler = scope.ServiceProvider.GetRequiredService<PreviewReconciler>();
                var result = await reconciler.ReconcileAsync(preview, cancellationToken);

                queue.Forget(name);
                metrics.Reconciled(Stopwatch.GetElapsedTime(started), success: true);
                if (result.RequeueAfter is { } delay)
                {
                    queue.AddAfter(name, delay);
                }

                var current = await cluster.GetPreviewAsync(name, cancellationToken);
                if (current?.Status?.Phase is { } phase)
                {
                    metrics.PhaseObserved(name, phase);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                metrics.Reconciled(Stopwatch.GetElapsedTime(started), success: false);
                var delay = queue.AddRateLimited(name);
                logger.ReconcileFailed(ex, name, queue.Failures(name), delay);
            }
            finally
            {
                queue.Done(name);
                metrics.SetQueueDepth(queue.Count);
            }
        }
    }
}
