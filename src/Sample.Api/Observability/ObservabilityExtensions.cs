using System.Reflection;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Sample.Application.Diagnostics;

namespace Sample.Api.Observability;

internal static class ObservabilityExtensions
{
    public const string ServiceName = "sample-api";

    // ASP.NET Core's default buckets jump from 250 ms to 500 ms; the SLO threshold (300 ms) must be a bucket
    // boundary, otherwise the SLI can only be approximated.
    private static readonly double[] LatencyBuckets =
        [0.005, 0.01, 0.025, 0.05, 0.1, 0.2, 0.3, 0.5, 0.75, 1, 2.5, 5, 10];

    private static readonly PathString[] UntracedPaths = ["/health", "/metrics"];

    public static string ServiceVersion { get; } =
        typeof(ObservabilityExtensions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        var telemetry = builder.Configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>() ?? new();
        builder.Services.Configure<TelemetryOptions>(builder.Configuration.GetSection(TelemetryOptions.SectionName));

        // Structured JSON on stdout, with the trace and span ids of the current activity in every entry.
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        });
        builder.Logging.Configure(options =>
            options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);
        builder.Logging.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
        });

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(ServiceName, serviceVersion: ServiceVersion)
                .AddAttributes([new("deployment.environment.name", builder.Environment.EnvironmentName)]))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                    options.Filter = context => !UntracedPaths.Any(p => context.Request.Path.StartsWithSegments(p)))
                .AddHttpClientInstrumentation()
                .AddNpgsql())
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(AbsenceMetrics.MeterName, "Npgsql")
                    .AddView(
                        "http.server.request.duration",
                        new ExplicitBucketHistogramConfiguration { Boundaries = LatencyBuckets });
                if (telemetry.PrometheusEndpoint)
                {
                    metrics.AddPrometheusExporter();
                }
            });

        // Exporting is opt-in through the standard variable, so tests and plain `dotnet run` stay silent.
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            otel.UseOtlpExporter();
        }

        return builder;
    }

    public static WebApplication MapObservabilityEndpoints(this WebApplication app)
    {
        var telemetry = app.Configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>() ?? new();
        if (telemetry.PrometheusEndpoint)
        {
            app.MapPrometheusScrapingEndpoint("/metrics").DisableRateLimiting();
        }

        return app;
    }
}
