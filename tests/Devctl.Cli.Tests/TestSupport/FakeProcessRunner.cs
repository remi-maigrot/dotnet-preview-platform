using Devctl.Cli.Infrastructure;

namespace Devctl.Cli.Tests.TestSupport;

/// <summary>Records every command and answers from a script; unscripted commands succeed with no output.</summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly List<(Func<string, bool> Match, ProcessResult Result)> _responses = [];

    public List<string> Commands { get; } = [];

    public FakeProcessRunner On(string commandPrefix, string output = "", int exitCode = 0, string error = "")
    {
        _responses.Insert(0, (c => c.StartsWith(commandPrefix, StringComparison.Ordinal), new ProcessResult(exitCode, output, error)));
        return this;
    }

    public FakeProcessRunner WithHealthyTools() => this
        .On("docker version", "28.3.3")
        .On("k3d version", "k3d version v5.9.0\nk3s version v1.36.4-k3s1 (default)")
        .On("kubectl version", "Client Version: v1.32.7\nKustomize Version: v5.5.0")
        .On("helm version", "v4.3.1+gc0d08d0")
        .On("trivy --version", "Version: 0.76.1")
        .On("kubeconform -v", "v0.8.0")
        .On("k6 version", "k6 v2.3.0");

    public Task<ProcessResult> RunAsync(string tool, IReadOnlyList<string> arguments, CancellationToken cancellationToken, string? workingDirectory = null)
    {
        var command = $"{tool} {string.Join(' ', arguments)}";
        Commands.Add(command);
        var response = _responses.FirstOrDefault(r => r.Match(command));
        return Task.FromResult(response.Result ?? new ProcessResult(0, string.Empty, string.Empty));
    }

    public Task<int> StreamAsync(string tool, IReadOnlyList<string> arguments, Action<string> onLine, CancellationToken cancellationToken, string? workingDirectory = null)
    {
        var result = RunAsync(tool, arguments, cancellationToken, workingDirectory).Result;
        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            onLine(line);
        }

        return Task.FromResult(result.ExitCode);
    }
}
