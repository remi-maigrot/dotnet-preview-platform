namespace Devctl.Cli.Infrastructure;

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>Runs external tools (docker, k3d, kubectl, helm). Faked in tests to assert what would be executed.</summary>
internal interface IProcessRunner
{
    /// <summary>Runs to completion and captures the output. Returns exit code 127 when the tool is not installed.</summary>
    Task<ProcessResult> RunAsync(string tool, IReadOnlyList<string> arguments, CancellationToken cancellationToken, string? workingDirectory = null);

    /// <summary>Runs and forwards each output line as it arrives (logs --follow, long builds in verbose mode).</summary>
    Task<int> StreamAsync(string tool, IReadOnlyList<string> arguments, Action<string> onLine, CancellationToken cancellationToken, string? workingDirectory = null);
}
