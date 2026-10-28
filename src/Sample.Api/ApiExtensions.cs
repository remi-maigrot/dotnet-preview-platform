using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Sample.Api.Endpoints;
using Sample.Api.Http;
using Sample.Api.Observability;
using Sample.Api.Security;
using Sample.Infrastructure.Persistence;

namespace Sample.Api;

internal static class ApiExtensions
{
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<HostOptions>(configuration.GetSection("Host"));
        // The API is stateless (bearer tokens, no cookies, no antiforgery): Data Protection keys never need to
        // survive a restart, so its "keys are not persisted" warnings at startup are noise.
        services.AddLogging(logging => logging.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Error));
        services.AddProblemDetails();
        services.AddValidation();
        services.AddOpenApi();
        services.AddJwtAuthentication(configuration);
        services.AddApiRateLimiting(configuration);

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

        // TLS terminates at the ingress controller; trust its X-Forwarded-* headers so generated links use https.
        // The NetworkPolicy only admits traffic from the ingress namespace, which is what makes this safe.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return services;
    }

    public static WebApplication UseApi(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
        app.MapObservabilityEndpoints();
        app.MapOpenApi();
        app.MapAbsenceRequestEndpoints();

        return app;
    }
}
