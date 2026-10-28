using System.Text.Json;
using Devctl.Cli.Commands;
using Devctl.Cli.Tests.TestSupport;

namespace Devctl.Cli.Tests;

public sealed class DoctorTests
{
    [Fact]
    public async Task AllToolsPresent_ExitsZero()
    {
        var cli = new DevctlHarness();
        cli.Runner.WithHealthyTools();

        var exit = await cli.RunAsync("doctor");

        exit.ShouldBe(ExitCodes.Success);
        cli.Text.ShouldContain("docker");
        cli.Text.ShouldContain("Everything devctl needs");
    }

    [Fact]
    public async Task MissingRequiredTool_ExitsWithPrerequisiteCodeAndAHint()
    {
        var cli = new DevctlHarness();
        cli.Runner.WithHealthyTools().On("k3d version", exitCode: 127);

        var exit = await cli.RunAsync("doctor");

        exit.ShouldBe(ExitCodes.PrerequisiteMissing);
        cli.Text.ShouldContain("brew install k3d");
    }

    [Fact]
    public async Task DockerDaemonDown_IsReportedAsNotRunning()
    {
        var cli = new DevctlHarness();
        cli.Runner.WithHealthyTools().On("docker version", exitCode: 1, error: "Cannot connect to the Docker daemon");

        var exit = await cli.RunAsync("doctor", "--output", "json");

        exit.ShouldBe(ExitCodes.PrerequisiteMissing);
        cli.Errors.ToString().ShouldContain("error: Some required tools");
        Checks(cli).Single(c => c.GetProperty("name").GetString() == "docker").GetProperty("status").GetString().ShouldBe("notRunning");
    }

    [Fact]
    public async Task OutdatedRequiredTool_Blocks()
    {
        var cli = new DevctlHarness();
        cli.Runner.WithHealthyTools().On("helm version", "v3.10.0+gce66412");

        (await cli.RunAsync("doctor")).ShouldBe(ExitCodes.PrerequisiteMissing);
    }

    [Fact]
    public async Task MissingOptionalTool_DoesNotBlock()
    {
        var cli = new DevctlHarness();
        cli.Runner.WithHealthyTools().On("trivy --version", exitCode: 127);

        (await cli.RunAsync("doctor")).ShouldBe(ExitCodes.Success);
    }

    [Fact]
    public async Task JsonOutput_ListsEveryTool()
    {
        var cli = new DevctlHarness();
        cli.Runner.WithHealthyTools();

        await cli.RunAsync("doctor", "-o", "json");

        Checks(cli).Select(c => c.GetProperty("name").GetString()).ShouldBe(Doctor.Requirements.Select(r => r.Name));
    }

    [Theory]
    [InlineData("k3d version v5.9.0", "5.9.0")]
    [InlineData("Client Version: v1.32.7\nKustomize Version: v5.5.0", "1.32.7")]
    [InlineData("v4.3.1+gc0d08d0", "4.3.1")]
    [InlineData("28.3", "28.3.0")]
    public void ParseVersion_FindsTheFirstVersion(string output, string expected) =>
        Doctor.ParseVersion(output)!.ToString().ShouldBe(expected);

    [Fact]
    public void ParseVersion_WithoutVersion_ReturnsNull() => Doctor.ParseVersion("no version here").ShouldBeNull();

    private static List<JsonElement> Checks(DevctlHarness cli) =>
        [.. JsonDocument.Parse(cli.Console.Output).RootElement.EnumerateArray()];
}
