using Devctl.Cli.Tests.TestSupport;

namespace Devctl.Cli.Tests;

public sealed class PlatformCommandsTests
{
    [Fact]
    public async Task Status_WithoutCluster_ExitsNotFound()
    {
        var cli = new DevctlHarness();
        cli.Runner.On("k3d cluster list", "[]");

        (await cli.RunAsync("status")).ShouldBe(ExitCodes.NotFound);
    }

    [Fact]
    public async Task Status_UnhealthyComponent_ExitsWithFailure()
    {
        var cli = new DevctlHarness();
        cli.Runner.On("k3d cluster list", """[{"name":"preview-platform"}]""");
        cli.Cluster.UnhealthyDeployments.Add("preview-operator");

        (await cli.RunAsync("status")).ShouldBe(ExitCodes.Failure);
        cli.Text.ShouldContain("0/1 ready");
    }

    [Fact]
    public async Task Status_Healthy_ExitsZero()
    {
        var cli = new DevctlHarness();
        cli.Runner.On("k3d cluster list", """[{"name":"preview-platform"}]""");

        (await cli.RunAsync("status", "-o", "json")).ShouldBe(ExitCodes.Success);
        cli.Console.Output.ShouldContain("\"components\"");
    }

    [Fact]
    public async Task Status_ObservabilityNotInstalled_IsNotAFailure()
    {
        var cli = new DevctlHarness();
        cli.Runner.On("k3d cluster list", """[{"name":"preview-platform"}]""");
        cli.Cluster.MissingDeployments.UnionWith(["otel-collector", "grafana", "kps-operator"]);

        (await cli.RunAsync("status")).ShouldBe(ExitCodes.Success);
        cli.Text.ShouldContain("not installed");
    }

    [Fact]
    public async Task Status_ObservabilityInstalledButUnhealthy_Fails()
    {
        var cli = new DevctlHarness();
        cli.Runner.On("k3d cluster list", """[{"name":"preview-platform"}]""");
        cli.Cluster.UnhealthyDeployments.Add("grafana");

        (await cli.RunAsync("status")).ShouldBe(ExitCodes.Failure);
    }

    [Fact]
    public async Task Status_OperatorMissing_Fails()
    {
        var cli = new DevctlHarness();
        cli.Runner.On("k3d cluster list", """[{"name":"preview-platform"}]""");
        cli.Cluster.MissingDeployments.Add("preview-operator");

        (await cli.RunAsync("status")).ShouldBe(ExitCodes.Failure);
    }

    [Fact]
    public async Task Down_WithoutCluster_HasNothingToDo()
    {
        var cli = new DevctlHarness();
        cli.Runner.On("k3d cluster list", "[]");

        (await cli.RunAsync("down")).ShouldBe(ExitCodes.Success);
        cli.Runner.Commands.ShouldNotContain("k3d cluster delete preview-platform");
    }

    [Fact]
    public async Task Down_DeletesTheCluster()
    {
        var cli = new DevctlHarness();
        cli.Runner.On("k3d cluster list", """[{"name":"preview-platform"}]""");

        (await cli.RunAsync("down")).ShouldBe(ExitCodes.Success);
        cli.Runner.Commands.ShouldContain("k3d cluster delete preview-platform");
    }

    [Theory]
    [InlineData(new[] { "logs", "api", "--pr", "42" }, "--namespace pr-42 logs deployment/sample-api")]
    [InlineData(new[] { "logs", "postgres" }, "--namespace local logs statefulset/postgresql")]
    [InlineData(new[] { "logs", "operator", "--follow" }, "--namespace preview-system logs deployment/preview-operator --all-containers --tail 200 --follow")]
    public async Task Logs_ResolveTheWorkload(string[] args, string expected)
    {
        var cli = new DevctlHarness();

        (await cli.RunAsync(args)).ShouldBe(ExitCodes.Success);
        cli.Runner.Commands.ShouldHaveSingleItem().ShouldContain(expected);
    }

    [Fact]
    public async Task Logs_KubectlFails_ExitsWithFailure()
    {
        var cli = new DevctlHarness();
        cli.Runner.On("kubectl", exitCode: 1);

        (await cli.RunAsync("logs", "api")).ShouldBe(ExitCodes.Failure);
    }

    [Fact]
    public async Task UnknownCommand_IsAUsageError()
    {
        var cli = new DevctlHarness();

        (await cli.RunAsync("deploy")).ShouldBe(ExitCodes.Usage);
    }
}
