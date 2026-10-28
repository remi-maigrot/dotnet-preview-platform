using System.Text.Json;
using Devctl.Cli.Kubernetes;
using Devctl.Cli.Tests.TestSupport;
using Preview.Operator.Crds;

namespace Devctl.Cli.Tests;

public sealed class PreviewTests
{
    [Fact]
    public async Task Create_WithImage_AppliesTheResourceAndWaitsForReady()
    {
        var cli = new DevctlHarness();

        var exit = await cli.RunAsync("preview", "create", "--pr", "42", "--ttl", "90m", "--image", "registry/sample-api:abc", "--seed");

        exit.ShouldBe(ExitCodes.Success);
        var applied = cli.Cluster.Applied.ShouldHaveSingleItem();
        applied.Metadata.Name.ShouldBe("pr-42");
        applied.Spec.PullRequest.ShouldBe(42);
        applied.Spec.Ttl.ShouldBe("90m");
        applied.Spec.Image.ShouldBe("registry/sample-api:abc");
        applied.Spec.SeedData.ShouldBeTrue();
        cli.Text.ShouldContain("Ready at http://pr-42.preview.localtest.me:8080");
    }

    [Fact]
    public async Task Create_WithoutImage_ReusesTheLocalEnvironmentImage()
    {
        var cli = new DevctlHarness();
        cli.Cluster.Previews["local"] = Summary(0, "local", "preview-registry.localhost:5050/sample-api:1234abcd");

        await cli.RunAsync("preview", "create", "--pr", "7");

        cli.Cluster.Applied.Single().Spec.Image.ShouldBe("preview-registry.localhost:5050/sample-api:1234abcd");
    }

    [Fact]
    public async Task Create_WithoutImageNorLocalEnvironment_IsAUsageError()
    {
        var cli = new DevctlHarness();

        (await cli.RunAsync("preview", "create", "--pr", "7")).ShouldBe(ExitCodes.Usage);
        cli.Text.ShouldContain("devctl up");
    }

    [Fact]
    public async Task Create_PreviewFails_ExitsWithFailureAndPointsToDiagnose()
    {
        var cli = new DevctlHarness();
        cli.Cluster.PhaseAfterApply = PreviewPhase.Failed;

        var exit = await cli.RunAsync("preview", "create", "--pr", "42", "--image", "registry/sample-api:abc");

        exit.ShouldBe(ExitCodes.Failure);
        cli.Text.ShouldContain("deployment failed");
        cli.Text.ShouldContain("diagnose --pr 42");
    }

    [Fact]
    public async Task Create_ReadyStatusOfThePreviousImage_IsNotTrusted()
    {
        var cli = new DevctlHarness();
        cli.Cluster.StaleStatusReads = 3;

        (await cli.RunAsync("preview", "create", "--pr", "42", "--image", "registry/sample-api:new")).ShouldBe(ExitCodes.Success);

        cli.Cluster.Reads.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Create_StillProvisioningAtTheTimeout_Fails()
    {
        var cli = new DevctlHarness();
        cli.Cluster.PhaseAfterApply = PreviewPhase.Provisioning;

        (await cli.RunAsync("preview", "create", "--pr", "42", "--image", "registry/sample-api:abc")).ShouldBe(ExitCodes.Failure);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    public async Task Create_InvalidPullRequest_IsAUsageError(string pr)
    {
        var cli = new DevctlHarness();

        (await cli.RunAsync("preview", "create", "--pr", pr, "--image", "x:y")).ShouldBe(ExitCodes.Usage);
        cli.Cluster.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task List_AsJson_ReturnsEveryPreview()
    {
        var cli = new DevctlHarness();
        cli.Cluster.Previews["pr-42"] = Summary(42, "pr-42", "registry/sample-api:abc");
        cli.Cluster.Previews["local"] = Summary(0, "local", "registry/sample-api:abc");

        (await cli.RunAsync("preview", "list", "-o", "json")).ShouldBe(ExitCodes.Success);

        var items = JsonDocument.Parse(cli.Console.Output).RootElement.EnumerateArray().ToList();
        items.Select(i => i.GetProperty("pullRequest").GetInt32()).ShouldBe([0, 42]);
    }

    [Fact]
    public async Task List_Empty_ExplainsHowToCreateOne()
    {
        var cli = new DevctlHarness();

        await cli.RunAsync("preview", "list");

        cli.Text.ShouldContain("devctl preview create --pr");
    }

    [Fact]
    public async Task Delete_Unknown_ExitsNotFound()
    {
        var cli = new DevctlHarness();

        (await cli.RunAsync("preview", "delete", "--pr", "9")).ShouldBe(ExitCodes.NotFound);
    }

    [Fact]
    public async Task Delete_Existing_WaitsUntilItIsGone()
    {
        var cli = new DevctlHarness();
        cli.Cluster.Previews["pr-9"] = Summary(9, "pr-9", "registry/sample-api:abc");

        (await cli.RunAsync("preview", "delete", "--pr", "9")).ShouldBe(ExitCodes.Success);
        cli.Cluster.Previews.ShouldBeEmpty();
    }

    private static PreviewSummary Summary(int pr, string name, string image) =>
        new(pr, name, image, PreviewPhase.Ready, $"http://{name}.preview.localtest.me:8080", null, null, null);
}
