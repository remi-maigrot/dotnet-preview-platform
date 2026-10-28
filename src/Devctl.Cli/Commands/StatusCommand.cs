using System.CommandLine;
using Devctl.Cli.Kubernetes;
using Spectre.Console;

namespace Devctl.Cli.Commands;

internal static class StatusCommand
{
    /// <summary>Deployments that make up the platform, checked in this order.</summary>
    internal static readonly (string Namespace, string Name)[] Components =
    [
        ("kube-system", "traefik"),
        (Platform.OperatorNamespace, "preview-operator"),
    ];

    public static Command Create(DevctlContext context, GlobalOptions globals)
    {
        var command = new Command("status", "Health of the platform components and the URLs of the API and the previews.");
        command.SetAction(async (result, cancellationToken) =>
        {
            var output = globals.CreateOutput(context, result);
            var clusters = await context.Runner.RunAsync("k3d", ["cluster", "list", "--output", "json"], cancellationToken);
            if (!clusters.StandardOutput.Contains(Platform.ClusterName, StringComparison.Ordinal))
            {
                output.Error("The local cluster does not exist.", "devctl up");
                return ExitCodes.NotFound;
            }

            var cluster = context.Cluster(result.GetValue(globals.Context)!);
            var components = new List<ComponentHealth>();
            foreach (var (ns, name) in Components)
            {
                components.Add(await cluster.GetDeploymentHealthAsync(ns, name, cancellationToken));
            }

            var previews = await cluster.ListPreviewsAsync(cancellationToken);
            if (output.IsJson)
            {
                output.WriteJson(new { components, previews });
            }
            else
            {
                var table = new Table().Border(TableBorder.Rounded).AddColumns("Component", "Namespace", "State");
                foreach (var component in components)
                {
                    table.AddRow(component.Name, component.Namespace,
                        component.Healthy ? $"[green]{component.Detail}[/]" : $"[red]{component.Detail}[/]");
                }

                context.Console.Write(table);
                if (previews.Count > 0)
                {
                    PreviewCommand.Report(output, previews);
                }
            }

            return components.All(c => c.Healthy) ? ExitCodes.Success : ExitCodes.Failure;
        });
        return command;
    }
}
