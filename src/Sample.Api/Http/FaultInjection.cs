using Microsoft.Extensions.Options;
using Sample.Api.Configuration;
using Sample.Api.Contracts;

namespace Sample.Api.Http;

internal static partial class FaultInjection
{
    public static IServiceCollection AddFaultInjection(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FaultInjectionOptions>()
            .Bind(configuration.GetSection(FaultInjectionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<FaultInjectionOptions>, FaultInjectionOptionsValidator>();
        return services;
    }

    /// <summary>
    /// Only the versioned API is affected: health probes keep passing, so Kubernetes keeps the pods and the
    /// failure shows up where users and the canary analysis see it.
    /// </summary>
    public static WebApplication UseFaultInjection(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<FaultInjectionOptions>>().Value;
        if (!options.Enabled)
        {
            return app;
        }

        LogEnabled(app.Logger, options.ErrorRatio, options.Delay);
        app.UseWhen(http => http.Request.Path.StartsWithSegments("/v1"), branch => branch.Use(async (http, next) =>
        {
            if (options.Delay > TimeSpan.Zero)
            {
                await Task.Delay(options.Delay, http.RequestAborted);
            }

            // Not a security decision: a fair coin is all a rehearsal needs.
            if (Random.Shared.NextDouble() < options.ErrorRatio)
            {
                http.Response.Headers["X-Fault-Injected"] = "true";
                await Problems.Create(StatusCodes.Status500InternalServerError, ErrorCodes.FaultInjected,
                    "Simulated failure (FaultInjection is enabled in this environment).").ExecuteAsync(http);
                return;
            }

            await next(http);
        }));
        return app;
    }

    [LoggerMessage(3000, LogLevel.Warning, "Fault injection is enabled: {ErrorRatio:P0} of API calls fail, each delayed by {Delay}")]
    private static partial void LogEnabled(ILogger logger, double errorRatio, TimeSpan delay);
}
