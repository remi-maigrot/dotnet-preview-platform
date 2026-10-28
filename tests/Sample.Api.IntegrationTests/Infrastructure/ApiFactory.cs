using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Sample.Api.IntegrationTests.Infrastructure;

/// <summary>Runs the real API in memory against the test container. Settings are overridable per test.</summary>
public sealed class ApiFactory(PostgresFixture postgres, IDictionary<string, string?>? overrides = null)
    : WebApplicationFactory<Program>
{
    public const string SigningKey = "integration-tests-signing-key-0123456789";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        var settings = new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = postgres.ConnectionString,
            ["Authentication:Jwt:SigningKey"] = SigningKey,
            ["RateLimiting:PermitLimit"] = "1000",
            ["Secrets:Path"] = Path.Combine(Path.GetTempPath(), "sample-api-no-secrets"),
        };
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
