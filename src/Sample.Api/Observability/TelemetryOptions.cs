namespace Sample.Api.Observability;

/// <summary>
/// Exporter settings use the standard OpenTelemetry environment variables (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>,
/// <c>OTEL_SERVICE_NAME</c>, <c>OTEL_RESOURCE_ATTRIBUTES</c>); this section only holds what they do not cover.
/// </summary>
internal sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    /// <summary>Exposes <c>/metrics</c> in the Prometheus text format, for debugging without a collector.</summary>
    public bool PrometheusEndpoint { get; set; } = true;
}
