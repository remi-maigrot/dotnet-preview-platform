using System.CommandLine;

namespace Devctl.Cli.Commands;

internal static class DownCommand
{
    public static Command Create(DevctlContext context, GlobalOptions globals)
    {
        var command = new Command("down", "Delete the local cluster, its registry and every preview in it.");
        command.SetAction(async (result, cancellationToken) =>
        {
            var output = globals.CreateOutput(context, result);
            var clusters = await context.Runner.RunAsync("k3d", ["cluster", "list", "--output", "json"], cancellationToken);
            if (!clusters.StandardOutput.Contains(Platform.ClusterName, StringComparison.Ordinal))
            {
                output.Info("No local cluster: nothing to delete.");
                return ExitCodes.Success;
            }

            try
            {
                await output.StatusAsync("Deleting the cluster…", () =>
                    context.RunAsync(output, "k3d", ["cluster", "delete", Platform.ClusterName], cancellationToken));
            }
            catch (StepFailedException ex)
            {
                output.Error(ex.Message, ex.Detail);
                return ExitCodes.Failure;
            }

            output.Success($"Cluster {Platform.ClusterName} deleted.");
            return ExitCodes.Success;
        });
        return command;
    }
}
