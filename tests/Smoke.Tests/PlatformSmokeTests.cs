using System.Net;

namespace Smoke.Tests;

public sealed class PlatformSmokeTests
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_IsGreen(string path)
    {
        using var client = SmokeEnvironment.Anonymous();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"{path} must be healthy once the environment is Ready");
    }

    [Fact]
    public async Task OpenApi_DescribesTheVersionedApi()
    {
        using var client = SmokeEnvironment.Anonymous();

        var document = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        document.ShouldContain("/v1/absence-requests");
    }

    [Fact]
    public async Task Api_RejectsAnonymousCalls()
    {
        using var client = SmokeEnvironment.Anonymous();

        var response = await client.GetAsync("/v1/absence-requests", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
