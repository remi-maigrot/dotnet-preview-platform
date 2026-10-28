using Devctl.Cli.Infrastructure;

namespace Devctl.Cli.Commands;

/// <summary>Runs a tool as one step of a command: traces it in verbose mode and throws with its stderr on failure.</summary>
internal static class Tools
{
    public static async Task<string> RunAsync(
        this DevctlContext context,
        Output output,
        string tool,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        string? workingDirectory = null)
    {
        output.Trace($"$ {tool} {string.Join(' ', arguments)}");
        var result = await context.Runner.RunAsync(tool, arguments, cancellationToken, workingDirectory);
        if (!result.Succeeded)
        {
            throw new StepFailedException(
                $"{tool} {string.Join(' ', arguments.Take(3))} failed (exit code {result.ExitCode})",
                result.StandardError.Trim().Length > 0 ? result.StandardError.Trim() : result.StandardOutput.Trim());
        }

        if (output.Verbose && result.StandardOutput.Length > 0)
        {
            output.Trace(result.StandardOutput.TrimEnd());
        }

        return result.StandardOutput;
    }
}

/// <summary>A step failed for a reason the user can act on; reported without a stack trace.</summary>
internal sealed class StepFailedException : Exception
{
    public StepFailedException()
    {
    }

    public StepFailedException(string message)
        : base(message)
    {
    }

    public StepFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public StepFailedException(string message, string detail)
        : base(message) => Detail = detail;

    public string? Detail { get; }
}
