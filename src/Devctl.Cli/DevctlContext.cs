using Devctl.Cli.Infrastructure;
using Devctl.Cli.Kubernetes;
using Spectre.Console;

namespace Devctl.Cli;

/// <summary>Dependencies of every command. Tests build it with a test console and fakes.</summary>
internal sealed record DevctlContext(
    IAnsiConsole Console,
    IProcessRunner Runner,
    Func<string, IPlatformClient> Cluster,
    RepositoryLocator Repository,
    TimeProvider Clock)
{
    /// <summary>Where errors go in JSON mode, so stdout stays a single parseable document.</summary>
    public TextWriter ErrorOutput { get; init; } = System.Console.Error;

    /// <summary>How long polling loops wait between two checks. Shortened in tests.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(3);

    public Func<string, string?> EnvironmentVariable { get; init; } = Environment.GetEnvironmentVariable;

    /// <summary>Sends the optional assistant requests. Replaced by a fake handler in tests.</summary>
    public HttpClient Http { get; init; } = new() { Timeout = TimeSpan.FromSeconds(90) };
}
