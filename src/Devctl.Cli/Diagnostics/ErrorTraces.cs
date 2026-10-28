using System.Globalization;
using System.Text.Json;
using Devctl.Cli.Infrastructure;

namespace Devctl.Cli.Diagnostics;

internal sealed record ErrorTrace(string TraceId, string Name, double DurationMs, DateTimeOffset Start);

/// <summary>
/// The latest failed requests of a preview, from Tempo. Read through the API server's service proxy with kubectl,
/// so it works with whatever authentication the kubeconfig uses and needs no port-forward. Null when Tempo is
/// not installed or does not answer: traces are a bonus, the report does not depend on them.
/// </summary>
internal static class ErrorTraces
{
    public static async Task<IReadOnlyList<ErrorTrace>?> FindAsync(
        IProcessRunner runner, string kubeContext, string previewNamespace, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runner);

        var query = Uri.EscapeDataString($"{{resource.k8s.namespace.name=\"{previewNamespace}\" && status=error}}");
        var start = now.AddHours(-1).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var end = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var path = $"/api/v1/namespaces/{Platform.ObservabilityNamespace}/services/tempo:3200/proxy/api/search?q={query}&limit=5&start={start}&end={end}";

        var result = await runner.RunAsync("kubectl", ["--context", kubeContext, "get", "--raw", path], cancellationToken);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(result.StandardOutput);
            if (!json.RootElement.TryGetProperty("traces", out var traces))
            {
                return [];
            }

            return [.. traces.EnumerateArray().Select(t => new ErrorTrace(
                t.GetProperty("traceID").GetString() ?? string.Empty,
                t.TryGetProperty("rootTraceName", out var name) ? name.GetString() ?? "?" : "?",
                t.TryGetProperty("durationMs", out var duration) ? duration.GetDouble() : 0,
                DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(t.GetProperty("startTimeUnixNano").GetString() ?? "0", CultureInfo.InvariantCulture) / 1_000_000)))];
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }
}
