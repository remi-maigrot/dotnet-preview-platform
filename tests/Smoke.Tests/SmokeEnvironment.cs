extern alias seeder;

using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using seeder::Preview.DataSeeder;

namespace Smoke.Tests;

/// <summary>The environment under test, read from SMOKE_* variables.</summary>
internal static class SmokeEnvironment
{
    public static Uri? BaseUrl { get; } =
        Uri.TryCreate(Environment.GetEnvironmentVariable("SMOKE_BASE_URL"), UriKind.Absolute, out var url) ? url : null;

    public static string? SigningKey { get; } = Environment.GetEnvironmentVariable("SMOKE_SIGNING_KEY");

    public static HttpClient Anonymous()
    {
        Assert.SkipWhen(BaseUrl is null, "SMOKE_BASE_URL is not set: smoke tests run against a deployed environment.");
        return new HttpClient { BaseAddress = BaseUrl, Timeout = TimeSpan.FromSeconds(15) };
    }

    public static HttpClient AsEmployee() => Authenticated(KnownAccounts.Employee, manager: false);

    public static HttpClient AsManager() => Authenticated(KnownAccounts.Manager, manager: true);

    private static HttpClient Authenticated(Guid subject, bool manager)
    {
        var client = Anonymous();
        Assert.SkipWhen(string.IsNullOrEmpty(SigningKey), "SMOKE_SIGNING_KEY is not set: authenticated scenarios are skipped.");

        var claims = new List<Claim> { new("sub", subject.ToString()) };
        if (manager)
        {
            claims.Add(new Claim("role", "manager"));
        }

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "preview-platform",
            Audience = "sample-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey!)), SecurityAlgorithms.HmacSha256),
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
