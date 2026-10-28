using System.CommandLine;
using System.Globalization;
using Devctl.Cli.Infrastructure;
using Devctl.Cli.Kubernetes;
using k8s.Models;
using Preview.Operator.Crds;
using Spectre.Console;

namespace Devctl.Cli.Commands;

internal static class UpCommand
{
    public static Command Create(DevctlContext context, GlobalOptions globals)
    {
        var httpPort = new Option<int>("--http-port")
        {
            Description = "Host port of the ingress (previews are served on it).",
            DefaultValueFactory = _ => Platform.DefaultHttpPort,
        };
        var skipBuild = new Option<bool>("--skip-build") { Description = "Reuse the images of the last build." };
        var skipObservability = new Option<bool>("--skip-observability")
        {
            Description = "Do not install Prometheus, Grafana, Tempo, Loki and the collector (saves about 1.5 GB of memory).",
        };

        var command = new Command("up", "Create the local cluster, install the platform and deploy the API of your working copy.")
        {
            httpPort,
            skipBuild,
            skipObservability,
        };
        command.SetAction(async (result, cancellationToken) =>
        {
            var output = globals.CreateOutput(context, result);
            var options = new UpOptions(
                result.GetValue(globals.Context)!,
                result.GetValue(httpPort),
                result.GetValue(skipBuild),
                result.GetValue(skipObservability));
            return await new LocalPlatform(context, output).UpAsync(options, cancellationToken);
        });
        return command;
    }
}

internal sealed record UpOptions(string KubeContext, int HttpPort, bool SkipBuild, bool SkipObservability = false);

/// <summary>
/// Brings the local platform up. Every step checks the current state first, so running <c>devctl up</c> again
/// after a failure or a code change resumes instead of starting over.
/// </summary>
internal sealed class LocalPlatform(DevctlContext context, Output output)
{
    public static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(6);

    public async Task<int> UpAsync(UpOptions options, CancellationToken cancellationToken)
    {
        var checks = await Doctor.CheckAsync(context.Runner, cancellationToken);
        if (checks.Where(c => c.IsBlocking).ToList() is { Count: > 0 } blocking)
        {
            output.Error($"Missing prerequisites: {string.Join(", ", blocking.Select(c => c.Name))}.", "Run devctl doctor for details.");
            return ExitCodes.PrerequisiteMissing;
        }

        var root = context.Repository.FindRoot();
        if (root is null)
        {
            output.Error("devctl up builds the platform from source and must run inside the repository.",
                $"cd into your clone of dotnet-preview-platform (the directory containing {RepositoryLocator.Marker}).");
            return ExitCodes.Usage;
        }

        try
        {
            var tag = await Step("Image tag", () => ImageTagAsync(root, cancellationToken));
            await Step("Cluster", () => EnsureClusterAsync(options, cancellationToken));
            if (!options.SkipBuild)
            {
                await Step("Images", () => BuildAndPushAsync(root, tag, cancellationToken));
            }

            await Step("Ingress", () => WaitForIngressAsync(options, cancellationToken));
            if (!options.SkipObservability)
            {
                // Before the API, so that its first requests are already traced.
                await Step("Observability", () => new ObservabilityStack(context, output).InstallAsync(root, options.KubeContext, cancellationToken));
            }

            await Step("Operator", () => InstallOperatorAsync(root, tag, options, cancellationToken));
            var local = await Step("API", () => DeployLocalEnvironmentAsync(tag, options, cancellationToken));

            if (output.IsJson)
            {
                output.WriteJson(new { tag, environment = local });
            }
            else
            {
                Summary(local, options);
            }

            return local.Phase == PreviewPhase.Ready ? ExitCodes.Success : ExitCodes.Failure;
        }
        catch (StepFailedException ex)
        {
            output.Error(ex.Message, ex.Detail);
            return ExitCodes.Failure;
        }
    }

    private async Task<string> ImageTagAsync(string root, CancellationToken cancellationToken)
    {
        var sha = (await context.RunAsync(output, "git", ["rev-parse", "--short=12", "HEAD"], cancellationToken, root)).Trim();
        var dirty = (await context.RunAsync(output, "git", ["status", "--porcelain"], cancellationToken, root)).Trim().Length > 0;

        // Uncommitted changes get a unique tag, so the new build is pulled instead of a cached image with the same name.
        return dirty
            ? $"{sha}-dirty-{context.Clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}"
            : sha;
    }

    private async Task<string> EnsureClusterAsync(UpOptions options, CancellationToken cancellationToken)
    {
        var clusters = await context.RunAsync(output, "k3d", ["cluster", "list", "--output", "json"], cancellationToken);
        if (clusters.Contains($"\"name\":\"{Platform.ClusterName}\"", StringComparison.Ordinal)
            || clusters.Contains($"\"name\": \"{Platform.ClusterName}\"", StringComparison.Ordinal))
        {
            await context.RunAsync(output, "k3d", ["cluster", "start", Platform.ClusterName], cancellationToken);
            return "already exists";
        }

        await context.RunAsync(output, "k3d",
        [
            "cluster", "create", Platform.ClusterName,
            "--agents", "1",
            "--registry-create", $"{Platform.RegistryName}:0.0.0.0:{Platform.RegistryPort.ToString(CultureInfo.InvariantCulture)}",
            "--port", $"{options.HttpPort.ToString(CultureInfo.InvariantCulture)}:80@loadbalancer",
            "--wait", "--timeout", "180s",
        ], cancellationToken);
        return "created";
    }

    private async Task<string> BuildAndPushAsync(string root, string tag, CancellationToken cancellationToken)
    {
        // Same build as CI: the Cake target, pointed at the local registry.
        await context.RunAsync(output, "dotnet",
            ["run", "--project", "build", "--", "--target", "Docker-Build", "--registry", Platform.RegistryFromHost, "--image-tag", tag],
            cancellationToken, root);

        foreach (var image in Platform.Images)
        {
            await context.RunAsync(output, "docker", ["push", "--quiet", $"{Platform.RegistryFromHost}/{image}:{tag}"], cancellationToken);
        }

        return $"{Platform.Images.Length} images pushed";
    }

    private async Task<string> WaitForIngressAsync(UpOptions options, CancellationToken cancellationToken)
    {
        // k3s installs Traefik asynchronously after the cluster reports ready: wait for the Deployment to exist first.
        var cluster = context.Cluster(options.KubeContext);
        var deadline = context.Clock.GetUtcNow() + TimeSpan.FromMinutes(3);
        while (!(await cluster.GetDeploymentHealthAsync("kube-system", "traefik", cancellationToken)).Healthy)
        {
            if (context.Clock.GetUtcNow() > deadline)
            {
                throw new StepFailedException("Traefik did not become ready within 3 minutes.",
                    $"kubectl --context {options.KubeContext} -n kube-system get pods");
            }

            await Task.Delay(context.PollInterval, context.Clock, cancellationToken);
        }

        return "traefik ready";
    }

    private async Task<string> InstallOperatorAsync(string root, string tag, UpOptions options, CancellationToken cancellationToken)
    {
        var overlay = Path.Combine(root, "artifacts", "devctl", "operator");
        Directory.CreateDirectory(overlay);
        await File.WriteAllTextAsync(Path.Combine(overlay, "kustomization.yaml"), OperatorOverlay(tag, options), cancellationToken);

        await context.RunAsync(output, "kubectl", ["--context", options.KubeContext, "apply", "--kustomize", overlay], cancellationToken);
        await context.RunAsync(output, "kubectl",
            ["--context", options.KubeContext, "--namespace", Platform.OperatorNamespace, "rollout", "status", "deployment/preview-operator", "--timeout", "180s"],
            cancellationToken);
        return "preview-operator running";
    }

    private async Task<PreviewSummary> DeployLocalEnvironmentAsync(string tag, UpOptions options, CancellationToken cancellationToken)
    {
        var preview = new PreviewEnvironment
        {
            Metadata = new V1ObjectMeta { Name = Platform.PreviewName(PreviewEnvironmentSpec.LocalEnvironment) },
            Spec = new PreviewEnvironmentSpec
            {
                PullRequest = PreviewEnvironmentSpec.LocalEnvironment,
                Image = $"{Platform.RegistryFromCluster}/sample-api:{tag}",
                Ttl = "72h",
                SeedData = true,
                CommitSha = tag,
            },
        };
        var cluster = context.Cluster(options.KubeContext);
        await cluster.ApplyPreviewAsync(preview, cancellationToken);
        return await PreviewWaiter.WaitForReadyAsync(context, cluster, preview.Metadata.Name, ReadyTimeout, cancellationToken);
    }

    private static string OperatorOverlay(string tag, UpOptions options)
    {
        var port = options.HttpPort.ToString(CultureInfo.InvariantCulture);
        return $"""
            # Generated by devctl up. Points the operator at the images of this build.
            apiVersion: kustomize.config.k8s.io/v1beta1
            kind: Kustomization
            resources:
              - ../../../deploy/operator
            images:
              - name: ghcr.io/remi-maigrot/preview-operator
                newName: {Platform.RegistryFromCluster}/preview-operator
                newTag: "{tag}"
            configMapGenerator:
              - name: preview-operator-config
                namespace: {Platform.OperatorNamespace}
                behavior: merge
                literals:
                  - Operator__DeployerImage={Platform.RegistryFromCluster}/sample-deployer:{tag}
                  - Operator__SeederImage={Platform.RegistryFromCluster}/sample-seeder:{tag}
                  - Operator__PublicPort={port}
                  - Operator__PreviewDomain={Platform.PreviewDomain}
            """;
    }

    private async Task<T> Step<T>(string name, Func<Task<T>> work)
    {
        var started = context.Clock.GetTimestamp();
        var result = await output.StatusAsync($"{name}…", work);
        var elapsed = context.Clock.GetElapsedTime(started);
        output.Success($"{name} [grey]({Markup.Escape(Describe(result))}, {elapsed.TotalSeconds:0}s)[/]");
        return result;
    }

    private static string Describe<T>(T result) => result switch
    {
        PreviewSummary preview => preview.Phase,
        null => "done",
        _ => result.ToString() ?? "done",
    };

    private void Summary(PreviewSummary local, UpOptions options)
    {
        var table = new Table().Border(TableBorder.Rounded).AddColumns("What", "Where");
        table.AddRow("API", Markup.Escape($"{local.Url}/v1/absence-requests"));
        table.AddRow("OpenAPI", Markup.Escape($"{local.Url}/openapi/v1.json"));
        if (!options.SkipObservability)
        {
            table.AddRow("Grafana", Markup.Escape($"http://{Platform.GrafanaHost}:{options.HttpPort.ToString(CultureInfo.InvariantCulture)}"));
        }

        table.AddRow("Previews", "devctl preview create --pr <number>");
        context.Console.Write(table);
        if (local.Phase != PreviewPhase.Ready)
        {
            output.Error($"The local environment is {local.Phase}: {local.Message}", "devctl logs operator, or kubectl get pe local -o yaml");
        }
    }
}
