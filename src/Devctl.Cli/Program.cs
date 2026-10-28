using Devctl.Cli;
using Devctl.Cli.Infrastructure;
using Devctl.Cli.Kubernetes;
using Spectre.Console;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // First Ctrl+C cancels cleanly (spinners stop, child processes are killed); a second one kills devctl.
    e.Cancel = true;
    cancellation.Cancel();
};

var context = new DevctlContext(
    AnsiConsole.Console,
    new CliWrapProcessRunner(),
    KubernetesPlatformClient.ForContext,
    new RepositoryLocator(Environment.CurrentDirectory),
    TimeProvider.System);

return await DevctlApp.RunAsync(context, args, cancellation.Token);
