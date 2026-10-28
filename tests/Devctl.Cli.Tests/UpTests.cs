using Devctl.Cli.Tests.TestSupport;
using Preview.Operator.Crds;

namespace Devctl.Cli.Tests;

public sealed class UpTests : IDisposable
{
    private readonly string _repository = Directory.CreateTempSubdirectory("devctl-repo-").FullName;
    private readonly DevctlHarness _cli;

    public UpTests()
    {
        File.WriteAllText(Path.Combine(_repository, "PreviewPlatform.slnx"), "<Solution />");
        var observability = Directory.CreateDirectory(Path.Combine(_repository, "deploy", "observability")).FullName;
        File.WriteAllText(Path.Combine(observability, "Chart.yaml"), """
            apiVersion: v2
            name: observability
            dependencies:
              - name: kube-prometheus-stack
                repository: https://prometheus-community.github.io/helm-charts
              - name: loki
                repository: https://grafana.github.io/helm-charts
              - name: tempo
                repository: https://grafana.github.io/helm-charts
            """);
        _cli = new DevctlHarness(_repository);
        _cli.Runner.WithHealthyTools()
            .On("git rev-parse", "1234567890ab\n")
            .On("git status --porcelain", string.Empty)
            .On("k3d cluster list", "[]");
    }

    [Fact]
    public async Task FreshMachine_CreatesTheClusterBuildsAndDeploys()
    {
        var exit = await _cli.RunAsync("up");

        exit.ShouldBe(ExitCodes.Success);
        var commands = _cli.Runner.Commands;
        commands.ShouldContain(c => c.StartsWith("k3d cluster create preview-platform", StringComparison.Ordinal)
            && c.Contains("--registry-create preview-registry.localhost:0.0.0.0:5050", StringComparison.Ordinal)
            && c.Contains("--port 8080:80@loadbalancer", StringComparison.Ordinal));
        commands.ShouldContain("dotnet run --project build -- --target Docker-Build --registry localhost:5050 --image-tag 1234567890ab");
        commands.Count(c => c.StartsWith("docker push", StringComparison.Ordinal)).ShouldBe(Platform.Images.Length);
        commands.ShouldContain(c => c.Contains("apply --kustomize", StringComparison.Ordinal));

        var local = _cli.Cluster.Applied.ShouldHaveSingleItem();
        local.Spec.PullRequest.ShouldBe(PreviewEnvironmentSpec.LocalEnvironment);
        local.Spec.Image.ShouldBe("preview-registry.localhost:5050/sample-api:1234567890ab");
        _cli.Text.ShouldContain("/v1/absence-requests");
    }

    [Fact]
    public async Task Overlay_PointsTheOperatorAtTheBuiltImages()
    {
        await _cli.RunAsync("up", "--http-port", "9090");

        var overlay = File.ReadAllText(Path.Combine(_repository, "artifacts", "devctl", "operator", "kustomization.yaml"));
        overlay.ShouldContain("newTag: \"1234567890ab\"");
        overlay.ShouldContain("Operator__DeployerImage=preview-registry.localhost:5050/sample-deployer:1234567890ab");
        overlay.ShouldContain("Operator__PublicPort=9090");
        _cli.Runner.Commands.ShouldContain(c => c.Contains("--port 9090:80@loadbalancer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ObservabilityStack_IsInstalledBeforeTheApi()
    {
        await _cli.RunAsync("up");

        var commands = _cli.Runner.Commands;
        var install = commands.FindIndex(c => c.StartsWith("helm upgrade --install observability", StringComparison.Ordinal));
        install.ShouldBeGreaterThan(-1);
        commands[install].ShouldContain("--namespace observability --create-namespace --wait");
        install.ShouldBeLessThan(commands.FindIndex(c => c.Contains("apply --kustomize", StringComparison.Ordinal)));
        _cli.Text.ShouldContain("http://grafana.preview.localtest.me:8080");
    }

    [Fact]
    public async Task ObservabilityStack_UsesAHelmConfigurationOfItsOwn()
    {
        await _cli.RunAsync("up");

        var repositories = _cli.Runner.Commands.Where(c => c.StartsWith("helm repo add", StringComparison.Ordinal)).ToList();
        repositories.Count.ShouldBe(2);
        repositories.ShouldContain(c => c.StartsWith("helm repo add grafana https://grafana.github.io/helm-charts", StringComparison.Ordinal));
        repositories.ShouldAllBe(c => c.Contains(Path.Combine("artifacts", "devctl", "helm", "repositories.yaml")));
        _cli.Runner.Commands.ShouldContain(c => c.StartsWith("helm dependency build", StringComparison.Ordinal)
            && c.Contains("--repository-config", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SkipObservability_InstallsNoChart()
    {
        await _cli.RunAsync("up", "--skip-observability");

        _cli.Runner.Commands.ShouldNotContain(c => c.StartsWith("helm upgrade", StringComparison.Ordinal)
            || c.StartsWith("helm repo", StringComparison.Ordinal));
        _cli.Text.ShouldNotContain("grafana.");
    }

    [Fact]
    public async Task ExistingCluster_IsStartedNotRecreated()
    {
        _cli.Runner.On("k3d cluster list", """[{"name":"preview-platform","serversCount":1}]""");

        await _cli.RunAsync("up");

        _cli.Runner.Commands.ShouldContain("k3d cluster start preview-platform");
        _cli.Runner.Commands.ShouldNotContain(c => c.StartsWith("k3d cluster create", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UncommittedChanges_GetAUniqueTag()
    {
        _cli.Runner.On("git status --porcelain", " M src/Sample.Api/Program.cs");

        await _cli.RunAsync("up");

        _cli.Cluster.Applied.Single().Spec.Image.ShouldContain(":1234567890ab-dirty-");
    }

    [Fact]
    public async Task SkipBuild_PushesNothing()
    {
        await _cli.RunAsync("up", "--skip-build");

        _cli.Runner.Commands.ShouldNotContain(c => c.StartsWith("docker push", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingPrerequisite_StopsBeforeTouchingAnything()
    {
        _cli.Runner.On("k3d version", exitCode: 127);

        (await _cli.RunAsync("up")).ShouldBe(ExitCodes.PrerequisiteMissing);
        _cli.Runner.Commands.ShouldNotContain(c => c.StartsWith("k3d cluster", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OutsideTheRepository_IsAUsageError()
    {
        var cli = new DevctlHarness(Directory.CreateTempSubdirectory("devctl-empty-").FullName);
        cli.Runner.WithHealthyTools();

        (await cli.RunAsync("up")).ShouldBe(ExitCodes.Usage);
        cli.Text.ShouldContain("PreviewPlatform.slnx");
    }

    [Fact]
    public async Task FailingStep_ReportsTheToolErrorAndStops()
    {
        _cli.Runner.On("k3d cluster create", exitCode: 1, error: "port 8080 is already allocated");

        var exit = await _cli.RunAsync("up");

        exit.ShouldBe(ExitCodes.Failure);
        _cli.Text.ShouldContain("port 8080 is already allocated");
        _cli.Runner.Commands.ShouldNotContain(c => c.StartsWith("docker push", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LocalEnvironmentFails_ExitsWithFailure()
    {
        _cli.Cluster.PhaseAfterApply = PreviewPhase.Failed;

        (await _cli.RunAsync("up")).ShouldBe(ExitCodes.Failure);
    }

    public void Dispose() => Directory.Delete(_repository, recursive: true);
}
