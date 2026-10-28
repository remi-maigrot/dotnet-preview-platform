using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using Sample.Api.Configuration;
using Sample.Api.Contracts;

namespace Sample.Api.Http;

internal static class RateLimitingExtensions
{
    public const string PerUserPolicy = "per-user";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(PerUserPolicy, http =>
            {
                var options = http.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
                var partition = http.User.Identity?.IsAuthenticated == true
                    ? $"user:{http.User.Identity.Name}"
                    : $"ip:{http.Connection.RemoteIpAddress}";
                return RateLimitPartition.GetTokenBucketLimiter(partition, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = options.PermitLimit,
                    TokensPerPeriod = options.PermitLimit,
                    ReplenishmentPeriod = options.Window,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            });
            limiter.OnRejected = async (context, cancellationToken) =>
            {
                var http = context.HttpContext;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                var problem = Problems.Create(StatusCodes.Status429TooManyRequests, ErrorCodes.RateLimited,
                    "Too many requests. Slow down and retry after the delay given in the Retry-After header.");
                await problem.ExecuteAsync(http);
            };
        });

        return services;
    }
}
