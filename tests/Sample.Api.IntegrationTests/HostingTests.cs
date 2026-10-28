using System.Net;
using Microsoft.Extensions.Options;
using Sample.Api.Contracts;
using Sample.Api.IntegrationTests.Infrastructure;

namespace Sample.Api.IntegrationTests;

public sealed class HostingTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpoints_AreAnonymousAndHealthy(string path)
    {
        await using var factory = new ApiFactory(postgres);

        var response = await factory.CreateClient().GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Ready_DatabaseUnreachable_Returns503ButLiveStaysUp()
    {
        await using var factory = new ApiFactory(postgres, new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=none;Username=none;Timeout=1",
            ["Database:MaxRetryCount"] = "0",
        });
        var client = factory.CreateClient();

        (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken)).StatusCode
            .ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).StatusCode
            .ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Metrics_AreExposedInPrometheusFormat()
    {
        await using var factory = new ApiFactory(postgres);
        var client = factory.CreateClient();
        await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        var body = await client.GetStringAsync("/metrics", TestContext.Current.CancellationToken);

        body.ShouldContain("http_server_request_duration_seconds_bucket");
        body.ShouldContain("le=\"0.3\"");
    }

    [Fact]
    public async Task OpenApiDocument_IsPublished()
    {
        await using var factory = new ApiFactory(postgres);

        var document = await factory.CreateClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        document.ShouldContain("/v1/absence-requests");
    }

    [Theory]
    [InlineData("Database:ConnectionString", "", "Database:ConnectionString is required")]
    [InlineData("Authentication:Jwt:SigningKey", "too-short", "at least 32 characters")]
    [InlineData("Features:ExternalHolidayCalendar", "true", "HolidayCalendar:BaseAddress is required")]
    public async Task Startup_InvalidConfiguration_RefusesToStartWithAClearMessage(string key, string value, string message)
    {
        await using var factory = new ApiFactory(postgres, new Dictionary<string, string?> { [key] = value });

        var exception = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        exception.Message.ShouldContain(message);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task RateLimit_Exceeded_Returns429WithRetryAfter()
    {
        await using var factory = new ApiFactory(postgres, new Dictionary<string, string?>
        {
            ["RateLimiting:PermitLimit"] = "2",
            ["RateLimiting:Window"] = "00:01:00",
        });
        var team = await Team.CreateAsync(postgres);
        var client = Team.As(factory.CreateClient(), team.Alice);

        for (var i = 0; i < 2; i++)
        {
            (await client.GetAsync("/v1/absence-requests", TestContext.Current.CancellationToken)).StatusCode
                .ShouldBe(HttpStatusCode.OK);
        }

        var limited = await client.GetAsync("/v1/absence-requests", TestContext.Current.CancellationToken);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.ShouldNotBeNull();
        (await limited.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain(ErrorCodes.RateLimited);
    }
}
