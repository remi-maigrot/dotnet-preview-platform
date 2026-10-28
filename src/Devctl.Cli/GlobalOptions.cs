using System.CommandLine;
using Devctl.Cli.Infrastructure;

namespace Devctl.Cli;

/// <summary>Options accepted by every command.</summary>
internal sealed class GlobalOptions
{
    public Option<bool> Verbose { get; } = new("--verbose", "-v")
    {
        Description = "Show the commands devctl runs and their output.",
        Recursive = true,
    };

    public Option<OutputFormat> Output { get; } = new("--output", "-o")
    {
        Description = "Output format: table for people, json for scripts.",
        DefaultValueFactory = _ => OutputFormat.Table,
        Recursive = true,
    };

    public Option<string> Context { get; } = new("--context")
    {
        Description = "kubeconfig context to talk to.",
        DefaultValueFactory = _ => Platform.KubeContext,
        Recursive = true,
    };

    public Output CreateOutput(DevctlContext context, ParseResult result) =>
        new(context.Console, result.GetValue(Output), result.GetValue(Verbose), context.ErrorOutput);
}
