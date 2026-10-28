using System.CommandLine;
using Spectre.Console;

namespace Devctl.Cli.Commands;

internal static class DoctorCommand
{
    public static Command Create(DevctlContext context, GlobalOptions globals)
    {
        var command = new Command("doctor", "Check that Docker, k3d, kubectl and helm are installed and recent enough.");
        command.SetAction(async (result, cancellationToken) =>
        {
            var output = globals.CreateOutput(context, result);
            var checks = await output.StatusAsync("Checking prerequisites…", () => Doctor.CheckAsync(context.Runner, cancellationToken));

            if (output.IsJson)
            {
                output.WriteJson(checks);
            }
            else
            {
                var table = new Table().Border(TableBorder.Rounded)
                    .AddColumns("Tool", "Required", "Found", "Status", "How to fix");
                foreach (var check in checks)
                {
                    table.AddRow(
                        check.Name,
                        check.Required ? check.Minimum : $"[grey]optional {check.Minimum}[/]",
                        check.Found ?? "-",
                        Describe(check),
                        Markup.Escape(check.Hint ?? string.Empty));
                }

                context.Console.Write(table);
            }

            if (checks.Any(c => c.IsBlocking))
            {
                output.Error("Some required tools are missing or too old.", "Fix the rows in red, then run devctl doctor again.");
                return ExitCodes.PrerequisiteMissing;
            }

            output.Success("Everything devctl needs is installed.");
            return ExitCodes.Success;
        });
        return command;
    }

    private static string Describe(ToolCheck check) => (check.Status, check.Required) switch
    {
        (ToolStatus.Ok, _) => "[green]ok[/]",
        (_, false) => $"[yellow]{check.Status.ToString().ToLowerInvariant()}[/]",
        _ => $"[red]{check.Status.ToString().ToLowerInvariant()}[/]",
    };
}
