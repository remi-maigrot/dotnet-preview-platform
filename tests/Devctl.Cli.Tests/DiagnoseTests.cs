using System.Net;
using System.Text.Json;
using Devctl.Cli.Diagnostics;
using Devctl.Cli.Tests.TestSupport;

namespace Devctl.Cli.Tests;

public sealed class DiagnoseTests
{
    private static readonly Dictionary<string, string> AssistantConfigured = new()
    {
        [AssistantSettings.EndpointVariable] = "https://llm.example.test/v1",
        [AssistantSettings.ModelVariable] = "some-model",
        [AssistantSettings.ApiKeyVariable] = "test-api-key",
    };

    [Fact]
    public async Task UnknownPreview_ExitsNotFound()
    {
        var cli = new DevctlHarness();

        (await cli.RunAsync("preview", "diagnose", "--pr", "9")).ShouldBe(ExitCodes.NotFound);
    }

    [Fact]
    public async Task CrashingApi_IsReportedWithTheLogOfTheRunThatCrashed()
    {
        var cli = new DevctlHarness();
        cli.Cluster.Snapshots["pr-12"] = Snapshot(Container("api", restarts: 4, waiting: "CrashLoopBackOff") with { LastExitCode = 139, LastTerminationReason = "Error" });
        cli.Cluster.Logs["sample-api-abc/api"] = "Unhandled exception. OptionsValidationException: Authentication:Jwt:Issuer is required";

        var exit = await cli.RunAsync("preview", "diagnose", "--pr", "12");

        exit.ShouldBe(ExitCodes.Failure);
        cli.Text.ShouldContain("sample-api-abc/api crashes");
        cli.Text.ShouldContain("last exit code 139");
        cli.Text.ShouldContain("Authentication:Jwt:Issuer is required");
        cli.Cluster.LogReads.ShouldBe([("sample-api-abc/api", true)]);
    }

    [Theory]
    [InlineData("ImagePullBackOff", "cannot be pulled")]
    [InlineData("CreateContainerConfigError", "cannot start")]
    public async Task WaitingContainer_IsExplained(string reason, string expected)
    {
        var cli = new DevctlHarness();
        cli.Cluster.Snapshots["pr-12"] = Snapshot(Container("api", waiting: reason));

        (await cli.RunAsync("preview", "diagnose", "--pr", "12")).ShouldBe(ExitCodes.Failure);
        cli.Text.ShouldContain(expected);
    }

    [Fact]
    public async Task QuotaWarning_IsReported()
    {
        var cli = new DevctlHarness();
        cli.Cluster.Snapshots["pr-12"] = Snapshot(Container("api")) with
        {
            Events = [new EventInfo(DateTimeOffset.UnixEpoch, "Warning", "FailedCreate", "ReplicaSet/sample-api-abc",
                "exceeded quota: preview-quota, requested: limits.memory=512Mi", 3)],
        };

        await cli.RunAsync("preview", "diagnose", "--pr", "12");

        cli.Text.ShouldContain("The namespace quota is exhausted");
    }

    [Fact]
    public async Task HealthyPreview_ExitsZeroAndSuggestsTheAssistant()
    {
        var cli = new DevctlHarness();
        cli.Cluster.Snapshots["pr-12"] = Snapshot(Container("api")) with { Phase = "Ready" };

        (await cli.RunAsync("preview", "diagnose", "--pr", "12")).ShouldBe(ExitCodes.Success);
        cli.Text.ShouldContain("Nothing wrong found");
        cli.Text.ShouldContain(AssistantSettings.EndpointVariable);
        cli.Http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ShowPayload_PrintsTheRedactedRequestAndSendsNothing()
    {
        var cli = new DevctlHarness(environment: AssistantConfigured);
        cli.Cluster.Snapshots["pr-12"] = Snapshot(Container("api", restarts: 1));
        cli.Cluster.Logs["sample-api-abc/api"] = "connecting with Host=postgresql;Username=app;Password=hunter2 for alice@corp.example";

        (await cli.RunAsync("preview", "diagnose", "--pr", "12", "--show-payload")).ShouldBe(ExitCodes.Success);

        cli.Http.Requests.ShouldBeEmpty();
        cli.Text.ShouldNotContain("hunter2");
        cli.Text.ShouldNotContain("alice@corp.example");
        cli.Text.ShouldNotContain("test-api-key");
        using var payload = JsonDocument.Parse(cli.Text);
        payload.RootElement.GetProperty("model").GetString().ShouldBe("some-model");
        payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!.ShouldContain("Password=<redacted>");
    }

    [Fact]
    public async Task Assistant_ReceivesTheRedactedReportAndItsAnswerIsPrinted()
    {
        var http = new FakeHttpHandler(body: """{"choices":[{"message":{"role":"assistant","content":"The JWT issuer is missing."}}]}""");
        var cli = new DevctlHarness(environment: AssistantConfigured, http: http);
        cli.Cluster.Snapshots["pr-12"] = Snapshot(Container("api", restarts: 1));
        cli.Cluster.Logs["sample-api-abc/api"] = "SigningKey=0123456789abcdef0123456789abcdef rejected";

        await cli.RunAsync("preview", "diagnose", "--pr", "12");

        var (request, body) = http.Requests.ShouldHaveSingleItem();
        request.RequestUri!.ToString().ShouldBe("https://llm.example.test/v1/chat/completions");
        request.Headers.Authorization!.Parameter.ShouldBe("test-api-key");
        body.ShouldNotContain("0123456789abcdef0123456789abcdef");
        cli.Text.ShouldContain("The JWT issuer is missing.");
    }

    [Fact]
    public async Task UnreachableAssistant_StillPrintsTheReport()
    {
        var cli = new DevctlHarness(environment: AssistantConfigured, http: new FakeHttpHandler(HttpStatusCode.BadGateway));
        cli.Cluster.Snapshots["pr-12"] = Snapshot(Container("api", restarts: 2));

        (await cli.RunAsync("preview", "diagnose", "--pr", "12")).ShouldBe(ExitCodes.Failure);

        cli.Text.ShouldContain("could not be reached");
        cli.Text.ShouldContain("crashes");
    }

    [Fact]
    public async Task EndpointWithoutModel_IsAUsageError()
    {
        var cli = new DevctlHarness(environment: new() { [AssistantSettings.EndpointVariable] = "https://llm.example.test/v1" });
        cli.Cluster.Snapshots["pr-12"] = Snapshot(Container("api"));

        (await cli.RunAsync("preview", "diagnose", "--pr", "12")).ShouldBe(ExitCodes.Usage);
    }

    [Fact]
    public async Task Json_HasTheFindings()
    {
        var cli = new DevctlHarness();
        cli.Cluster.Snapshots["pr-12"] = Snapshot(Container("api", waiting: "ImagePullBackOff"));

        await cli.RunAsync("preview", "diagnose", "--pr", "12", "-o", "json");

        using var json = JsonDocument.Parse(cli.Text);
        json.RootElement.GetProperty("findings")[0].GetProperty("title").GetString()!.ShouldContain("cannot be pulled");
    }

    private static ContainerInfo Container(string name, int restarts = 0, string? waiting = null) =>
        new(name, Ready: waiting is null && restarts == 0, restarts, waiting, waiting is null ? null : $"{waiting}: details");

    private static PreviewSnapshot Snapshot(ContainerInfo api) => new(
        "pr-12", "pr-12", "Provisioning", "Waiting for the deployment", "registry.example/sample-api:abc",
        [], [new PodInfo("sample-api-abc", "Running", [api])], [], []);
}
