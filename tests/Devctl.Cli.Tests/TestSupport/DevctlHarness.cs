using Devctl.Cli.Infrastructure;
using Microsoft.Extensions.Time.Testing;
using Spectre.Console.Testing;

namespace Devctl.Cli.Tests.TestSupport;

/// <summary>Runs devctl in-process against fakes and captures what it prints.</summary>
internal sealed class DevctlHarness
{
    public DevctlHarness(string? repositoryRoot = null)
    {
        Console = new TestConsole();
        Console.Profile.Width = 200;
        Context = new DevctlContext(
            Console,
            Runner,
            _ => Cluster,
            new RepositoryLocator(repositoryRoot ?? Path.GetTempPath()),
            new FakeTimeProvider(DateTimeOffset.UnixEpoch) { AutoAdvanceAmount = TimeSpan.FromSeconds(1) })
        { PollInterval = TimeSpan.Zero, ErrorOutput = Errors };
    }

    public TestConsole Console { get; }

    public StringWriter Errors { get; } = new();

    public FakeProcessRunner Runner { get; } = new();

    public FakePlatformClient Cluster { get; } = new();

    public DevctlContext Context { get; }

    public string Text => Console.Output;

    public Task<int> RunAsync(params string[] args) => DevctlApp.RunAsync(Context, args, TestContext.Current.CancellationToken);
}
