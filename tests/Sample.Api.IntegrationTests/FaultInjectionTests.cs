using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sample.Api.Contracts;
using Sample.Api.IntegrationTests.Infrastructure;

namespace Sample.Api.IntegrationTests;

/// <summary>The fault injection used to rehearse a canary rollback: it breaks the API on purpose, never in Production.</summary>
public sealed class FaultInjectionTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Enabled_FailsApiCallsButNotHealthProbes()
    {
        await using var factory = new ApiFactory(postgres, new Dictionary<string, string?>
        {
            ["FaultInjection:Enabled"] = "true",
            ["FaultInjection:ErrorRatio"] = "1",
        });
        var team = await Team.CreateAsync(postgres);
        var client = Team.As(factory.CreateClient(), team.Alice);

        var response = await client.GetAsync("/v1/absence-requests", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Headers.GetValues("X-Fault-Injected").ShouldBe(["true"]);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
        problem!.Extensions["code"]!.ToString().ShouldBe(ErrorCodes.FaultInjected);
        (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Disabled_ByDefault()
    {
        await using var factory = new ApiFactory(postgres);
        var team = await Team.CreateAsync(postgres);

        var response = await Team.As(factory.CreateClient(), team.Alice).GetAsync("/v1/absence-requests", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("X-Fault-Injected").ShouldBeFalse();
    }

    [Fact]
    public async Task EnabledInProduction_TheApiRefusesToStart()
    {
        await using var factory = new ApiFactory(postgres, new Dictionary<string, string?>
        {
            ["FaultInjection:Enabled"] = "true",
            ["FaultInjection:ErrorRatio"] = "0.5",
        }, environment: "Production");

        var error = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        error.Message.ShouldContain("FaultInjection");
    }
}
