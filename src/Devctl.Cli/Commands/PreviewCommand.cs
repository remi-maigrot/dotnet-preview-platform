using System.CommandLine;
using System.Globalization;
using Devctl.Cli.Infrastructure;
using Devctl.Cli.Kubernetes;
using k8s.Models;
using Preview.Operator.Crds;
using Spectre.Console;

namespace Devctl.Cli.Commands;

internal static class PreviewCommand
{
    public static Command Create(DevctlContext context, GlobalOptions globals)
    {
        var command = new Command("preview", "Manage preview environments (one isolated namespace per pull request).");
        command.Subcommands.Add(CreateCommand(context, globals));
        command.Subcommands.Add(ListCommand(context, globals));
        command.Subcommands.Add(DeleteCommand(context, globals));
        return command;
    }

    public static Option<int> PullRequestOption() => new("--pr")
    {
        Description = "Pull request number.",
        Required = true,
        Validators =
        {
            result =>
            {
                if (result.GetValueOrDefault<int>() < 1)
                {
                    result.AddError("--pr must be a positive pull request number.");
                }
            },
        },
    };

    private static Command CreateCommand(DevctlContext context, GlobalOptions globals)
    {
        var pr = PullRequestOption();
        var ttl = new Option<string>("--ttl")
        {
            Description = "Lifetime, as a duration: 30m, 2h, 1h30m. The platform caps it.",
            DefaultValueFactory = _ => "2h",
        };
        var image = new Option<string?>("--image")
        {
            Description = "API image to deploy. Defaults to the image of the local environment (your last devctl up).",
        };
        var seed = new Option<bool>("--seed") { Description = "Load pseudonymised sample data." };
        var noWait = new Option<bool>("--no-wait") { Description = "Return as soon as the resource is created." };

        var command = new Command("create", "Create or update the preview environment of a pull request.") { pr, ttl, image, seed, noWait };
        command.SetAction(async (result, cancellationToken) =>
        {
            var output = globals.CreateOutput(context, result);
            var cluster = context.Cluster(result.GetValue(globals.Context)!);
            var number = result.GetValue(pr);

            var imageReference = result.GetValue(image);
            if (imageReference is null)
            {
                var local = await cluster.GetPreviewAsync(Platform.PreviewName(PreviewEnvironmentSpec.LocalEnvironment), cancellationToken);
                if (local is null)
                {
                    output.Error("No image given and no local environment to take it from.", "Run devctl up first, or pass --image.");
                    return ExitCodes.Usage;
                }

                imageReference = local.Image;
            }

            var preview = new PreviewEnvironment
            {
                Metadata = new V1ObjectMeta { Name = Platform.PreviewName(number) },
                Spec = new PreviewEnvironmentSpec
                {
                    PullRequest = number,
                    Image = imageReference,
                    Ttl = result.GetValue(ttl)!,
                    SeedData = result.GetValue(seed),
                },
            };
            await cluster.ApplyPreviewAsync(preview, cancellationToken);
            output.Success($"PreviewEnvironment {preview.Metadata.Name} applied ({Markup.Escape(imageReference)}).");

            if (result.GetValue(noWait))
            {
                return ExitCodes.Success;
            }

            var state = await output.StatusAsync($"Waiting for {preview.Metadata.Name} to be ready…", () =>
                PreviewWaiter.WaitForReadyAsync(context, cluster, preview.Metadata.Name, LocalPlatform.ReadyTimeout, cancellationToken));
            Report(output, [state]);

            if (state.Phase == PreviewPhase.Ready)
            {
                output.Success($"Ready at {Markup.Escape(state.Url ?? string.Empty)}");
                return ExitCodes.Success;
            }

            output.Error($"{preview.Metadata.Name} is {state.Phase}: {state.Message ?? "still provisioning after the timeout"}",
                $"devctl preview diagnose --pr {number.ToString(CultureInfo.InvariantCulture)}");
            return ExitCodes.Failure;
        });
        return command;
    }

    private static Command ListCommand(DevctlContext context, GlobalOptions globals)
    {
        var command = new Command("list", "List preview environments.");
        command.SetAction(async (result, cancellationToken) =>
        {
            var output = globals.CreateOutput(context, result);
            var previews = await context.Cluster(result.GetValue(globals.Context)!).ListPreviewsAsync(cancellationToken);
            if (!output.IsJson && previews.Count == 0)
            {
                output.Info("No preview environment. Create one with: devctl preview create --pr <number>");
                return ExitCodes.Success;
            }

            Report(output, previews);
            return ExitCodes.Success;
        });
        return command;
    }

    private static Command DeleteCommand(DevctlContext context, GlobalOptions globals)
    {
        var pr = PullRequestOption();
        var noWait = new Option<bool>("--no-wait") { Description = "Do not wait for the namespace to be deleted." };
        var command = new Command("delete", "Delete the preview environment of a pull request and everything in it.") { pr, noWait };
        command.SetAction(async (result, cancellationToken) =>
        {
            var output = globals.CreateOutput(context, result);
            var cluster = context.Cluster(result.GetValue(globals.Context)!);
            var name = Platform.PreviewName(result.GetValue(pr));

            if (!await cluster.DeletePreviewAsync(name, cancellationToken))
            {
                output.Error($"No preview environment named {name}.", "devctl preview list");
                return ExitCodes.NotFound;
            }

            if (!result.GetValue(noWait)
                && !await output.StatusAsync($"Deleting {name}…", () =>
                    PreviewWaiter.WaitForDeletionAsync(context, cluster, name, TimeSpan.FromMinutes(3), cancellationToken)))
            {
                output.Error($"{name} is still being deleted after 3 minutes.", $"kubectl get pe {name} -o yaml  (look at the finalizer)");
                return ExitCodes.Failure;
            }

            output.Success($"{name} deleted.");
            return ExitCodes.Success;
        });
        return command;
    }

    internal static void Report(Output output, IReadOnlyList<PreviewSummary> previews)
    {
        if (output.IsJson)
        {
            output.WriteJson(previews);
            return;
        }

        var table = new Table().Border(TableBorder.Rounded).AddColumns("PR", "Phase", "URL", "Expires", "Image");
        foreach (var preview in previews)
        {
            table.AddRow(
                preview.PullRequest == 0 ? "local" : preview.PullRequest.ToString(CultureInfo.InvariantCulture),
                Phase(preview.Phase),
                Markup.Escape(preview.Url ?? "-"),
                preview.ExpiresAt?.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "-",
                Markup.Escape(ShortImage(preview.Image)));
        }

        output.Console.Write(table);
    }

    /// <summary>name:tag without the registry, which is the same for every row.</summary>
    private static string ShortImage(string image) => image[(image.LastIndexOf('/') + 1)..];

    private static string Phase(string phase) => phase switch
    {
        PreviewPhase.Ready => "[green]Ready[/]",
        PreviewPhase.Failed => "[red]Failed[/]",
        PreviewPhase.Expired => "[grey]Expired[/]",
        _ => $"[yellow]{phase}[/]",
    };
}
