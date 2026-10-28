using System.CommandLine;
using Devctl.Cli.Commands;
using Spectre.Console;

namespace Devctl.Cli;

internal static class DevctlApp
{
    public static RootCommand Create(DevctlContext context)
    {
        var globals = new GlobalOptions();
        var root = new RootCommand("devctl: local cluster, preview environments and diagnostics for the preview platform.")
        {
            globals.Verbose,
            globals.Output,
            globals.Context,
        };
        root.Subcommands.Add(DoctorCommand.Create(context, globals));
        root.Subcommands.Add(UpCommand.Create(context, globals));
        root.Subcommands.Add(DownCommand.Create(context, globals));
        root.Subcommands.Add(StatusCommand.Create(context, globals));
        root.Subcommands.Add(PreviewCommand.Create(context, globals));
        root.Subcommands.Add(LogsCommand.Create(context, globals));
        return root;
    }

    /// <summary>Parses and runs. Usage errors exit with 2; unexpected errors are reported without a stack trace unless --verbose.</summary>
    public static async Task<int> RunAsync(DevctlContext context, string[] args, CancellationToken cancellationToken)
    {
        var root = Create(context);
        var parsed = root.Parse(args);
        if (parsed.Errors.Count > 0)
        {
            foreach (var error in parsed.Errors)
            {
                context.Console.MarkupLine($"[red]✗ {Markup.Escape(error.Message)}[/]");
            }

            context.Console.MarkupLine("[grey]Run devctl --help for usage.[/]");
            return ExitCodes.Usage;
        }

        try
        {
            return await parsed.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            context.Console.MarkupLine("[yellow]Cancelled.[/]");
            return ExitCodes.Failure;
        }
        catch (Exception ex) when (ex is k8s.Autorest.HttpOperationException or HttpRequestException or k8s.Exceptions.KubeConfigException)
        {
            context.Console.MarkupLine($"[red]✗ Cannot reach the cluster: {Markup.Escape(ex.Message)}[/]");
            context.Console.MarkupLine("[grey]Is it running? devctl status, or devctl up to create it.[/]");
            return ExitCodes.Failure;
        }
    }
}
