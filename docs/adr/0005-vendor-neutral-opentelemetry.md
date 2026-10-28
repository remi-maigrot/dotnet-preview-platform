# 5. OpenTelemetry end to end, through a collector

- Status: accepted
- Date: 2026-10-28

## Context

The API and the operator need traces, metrics and logs, correlated with each other. Tying them to a vendor's
SDK makes the backend hard to change, and every team ends up instrumenting differently.

## Decision

- Applications use the OpenTelemetry .NET SDK and its standard environment variables
  (`OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES`, `OTEL_METRIC_EXPORT_INTERVAL`).
  They know only OTLP and the address of a collector.
- The OpenTelemetry Collector routes each signal to its backend: metrics to Prometheus (OTLP receiver), traces
  to Tempo, logs to Loki. It also drops health-probe spans. Changing a backend is a collector configuration
  change.
- Resource attributes carry the context: namespace, pull request number, version, pod. Prometheus and Loki
  promote them to labels, so every query can be scoped to one environment.
- Logs are structured (JSON on stdout, OTLP to the collector) and carry the trace and span ids. Metrics carry
  exemplars. Grafana links metrics, traces and logs both ways.

## Consequences

- One instrumentation for any backend that speaks OTLP, hosted or self-run.
- The collector is in the data path: if it is down, telemetry is lost (the SDK retries briefly, then drops).
  Applications keep working, and JSON logs on stdout remain available through `kubectl logs`.
- OTLP metrics are pushed, so Prometheus no longer notices a dead target by itself. The operator, a stable
  component, is scraped instead, which gives `up == 0` for free.

## Alternatives considered

- **Prometheus client library and scraping for the API.** Mature and simple, but metrics only, and preview
  namespaces come and go faster than scrape configuration is worth maintaining.
- **A vendor agent.** Rich features, lock-in, and a different instrumentation per backend.
