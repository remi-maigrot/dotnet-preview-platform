using System.ComponentModel;
using CliWrap;
using CliWrap.Buffered;

namespace Devctl.Cli.Infrastructure;

internal sealed class CliWrapProcessRunner : IProcessRunner
{
    public const int NotInstalled = 127;

    public async Task<ProcessResult> RunAsync(string tool, IReadOnlyList<string> arguments, CancellationToken cancellationToken, string? workingDirectory = null)
    {
        try
        {
            var result = await Command(tool, arguments, workingDirectory).ExecuteBufferedAsync(cancellationToken);
            return new ProcessResult(result.ExitCode, result.StandardOutput, result.StandardError);
        }
        catch (Win32Exception)
        {
            return new ProcessResult(NotInstalled, string.Empty, $"{tool}: command not found");
        }
    }

    public async Task<int> StreamAsync(string tool, IReadOnlyList<string> arguments, Action<string> onLine, CancellationToken cancellationToken, string? workingDirectory = null)
    {
        try
        {
            var result = await Command(tool, arguments, workingDirectory)
                .WithStandardOutputPipe(PipeTarget.ToDelegate(onLine))
                .WithStandardErrorPipe(PipeTarget.ToDelegate(onLine))
                .ExecuteAsync(cancellationToken);
            return result.ExitCode;
        }
        catch (Win32Exception)
        {
            onLine($"{tool}: command not found");
            return NotInstalled;
        }
    }

    private static Command Command(string tool, IReadOnlyList<string> arguments, string? workingDirectory) =>
        CliWrap.Cli.Wrap(tool)
            .WithArguments(arguments)
            .WithWorkingDirectory(workingDirectory ?? Environment.CurrentDirectory)
            .WithValidation(CommandResultValidation.None);
}
