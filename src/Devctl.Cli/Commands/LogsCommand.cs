using System.CommandLine;
using System.Globalization;
using Spectre.Console;

namespace Devctl.Cli.Commands;

internal static class LogsCommand
{
    /// <summary>Service name → where its logs live. Preview services are resolved in the namespace of --pr.</summary>
    internal static readonly IReadOnlyDictionary<string, (string? Namespace, string Workload)> Services =
        new Dictionary<string, (string?, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["api"] = (null, "deployment/sample-api"),
            ["postgres"] = (null, "statefulset/postgresql"),
            ["operator"] = (Platform.OperatorNamespace, "deployment/preview-operator"),
            ["ingress"] = ("kube-system", "deployment/traefik"),
        };

    public static Command Create(DevctlContext context, GlobalOptions globals)
    {
        var service = new Argument<string>("service")
        {
            Description = $"One of: {string.Join(", ", Services.Keys)}.",
        };
        service.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string>();
            if (value is not null && !Services.ContainsKey(value))
            {
                result.AddError($"Unknown service '{value}'. Expected one of: {string.Join(", ", Services.Keys)}.");
            }
        });
        var pr = new Option<int>("--pr")
        {
            Description = "Pull request of the preview (api and postgres). Defaults to the local environment.",
            DefaultValueFactory = _ => 0,
        };
        var follow = new Option<bool>("--follow", "-f") { Description = "Keep streaming new lines." };
        var tail = new Option<int>("--tail") { Description = "Number of past lines to show.", DefaultValueFactory = _ => 200 };

        var command = new Command("logs", "Show the logs of a platform service.") { service, pr, follow, tail };
        command.SetAction(async (result, cancellationToken) =>
        {
            var (ns, workload) = Services[result.GetValue(service)!];
            var arguments = new List<string>
            {
                "--context", result.GetValue(globals.Context)!,
                "--namespace", ns ?? Platform.NamespaceOf(result.GetValue(pr)),
                "logs", workload,
                "--all-containers",
                "--tail", result.GetValue(tail).ToString(CultureInfo.InvariantCulture),
            };
            if (result.GetValue(follow))
            {
                arguments.Add("--follow");
            }

            var exitCode = await context.Runner.StreamAsync("kubectl", arguments, line => context.Console.WriteLine(line), cancellationToken);
            if (exitCode != 0)
            {
                context.Console.MarkupLine($"[red]✗ kubectl logs failed (exit code {exitCode}).[/] [grey]Is the service deployed? devctl status[/]");
                return ExitCodes.Failure;
            }

            return ExitCodes.Success;
        });
        return command;
    }
}
