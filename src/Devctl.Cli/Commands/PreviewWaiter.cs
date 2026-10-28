using Devctl.Cli.Kubernetes;
using Preview.Operator.Crds;

namespace Devctl.Cli.Commands;

internal static class PreviewWaiter
{
    /// <summary>Polls until the preview is Ready or Failed. A timeout returns the last state seen instead of throwing.</summary>
    public static async Task<PreviewSummary> WaitForReadyAsync(
        DevctlContext context,
        IPlatformClient cluster,
        string name,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = context.Clock.GetUtcNow() + timeout;
        while (true)
        {
            var preview = await cluster.GetPreviewAsync(name, cancellationToken)
                ?? throw new StepFailedException($"PreviewEnvironment {name} disappeared while waiting for it.");
            // A Ready status from before the last apply says nothing about the new image: wait for the operator to catch up.
            var settled = preview.IsUpToDate && preview.Phase is PreviewPhase.Ready or PreviewPhase.Failed;
            if (settled || preview.Phase == PreviewPhase.Expired || context.Clock.GetUtcNow() >= deadline)
            {
                return preview;
            }

            await Task.Delay(context.PollInterval, context.Clock, cancellationToken);
        }
    }

    public static async Task<bool> WaitForDeletionAsync(
        DevctlContext context,
        IPlatformClient cluster,
        string name,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = context.Clock.GetUtcNow() + timeout;
        while (await cluster.GetPreviewAsync(name, cancellationToken) is not null)
        {
            if (context.Clock.GetUtcNow() >= deadline)
            {
                return false;
            }

            await Task.Delay(context.PollInterval, context.Clock, cancellationToken);
        }

        return true;
    }
}
