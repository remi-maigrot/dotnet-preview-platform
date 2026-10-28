using System.CommandLine;
using Devctl.Cli.Kubernetes;
using Spectre.Console;

namespace Devctl.Cli.Commands;

internal static class StatusCommand
{
    /// <summary>
    /// Deployments that make up the platform, checked in this order. The observability stack is optional
    /// (<c>devctl up --skip-observability</c>): missing, it is reported but does not fail the command.
    /// </summary>
    internal static readonly (string Namespace, string Name, bool Optional)[] Components =
    [
        ("kube-system", "traefik", false),
        (Platform.OperatorNamespace, "preview-operator", false),
        (Platform.ObservabilityNamespace, "otel-collector", true),
        (Platform.ObservabilityNamespace, "grafana", true),
        (Platform.ObservabilityNamespace, "kps-operator", true),
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
            var failing = false;
            foreach (var (ns, name, optional) in Components)
            {
                var health = await cluster.GetDeploymentHealthAsync(ns, name, cancellationToken);
                components.Add(health);
                failing |= !health.Healthy && (health.Installed || !optional);
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
                    var color = component.Healthy ? "green" : component.Installed ? "red" : "grey";
                    table.AddRow(component.Name, component.Namespace, $"[{color}]{component.Detail}[/]");
                }

                context.Console.Write(table);
                if (previews.Count > 0)
                {
                    PreviewCommand.Report(output, previews);
                }
            }

            return failing ? ExitCodes.Failure : ExitCodes.Success;
        });
        return command;
    }
}
