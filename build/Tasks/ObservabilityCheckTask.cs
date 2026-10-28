using System.Text.Json;
using Cake.Common.IO;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Frosting;

namespace Build.Tasks;

[TaskName("Observability-Check")]
[TaskDescription("Tests the alert rules with promtool, checks the dashboards and renders the observability chart")]
public sealed class ObservabilityCheckTask : FrostingTask<BuildContext>
{
    // promtool ships in the Prometheus image: no local install, and the same version everywhere.
    private const string PromtoolImage =
        "prom/prometheus:v3.15.1@sha256:fa3302279710be2f21a8301ec17a8b4f2301fd829bd113839b3b6b02b6f191a2";

    private static readonly HashSet<string> KnownDatasources = ["prometheus", "loki", "tempo", "-- Grafana --"];

    public override void Run(BuildContext context)
    {
        var chart = context.MakeAbsolute(context.ObservabilityChartDirectory);
        TestRules(context, chart);
        CheckDashboards(context, chart);
        RenderChart(context, chart);
    }

    private static void TestRules(BuildContext context, DirectoryPath chart)
    {
        var rules = context.GetFiles(chart.FullPath + "/rules/*.rules.yaml").Select(f => f.GetFilename().FullPath).ToList();
        var tests = context.GetFiles(chart.FullPath + "/rules/tests/*.yaml").Select(f => f.GetFilename().FullPath).ToList();
        if (rules.Count == 0 || tests.Count == 0)
        {
            throw new CakeException("No rule files or no rule tests found under deploy/observability/rules.");
        }

        ProcessArgumentBuilder Promtool(string workdir) => new ProcessArgumentBuilder()
            .Append("run --rm")
            .AppendSwitchQuoted("--volume", $"{chart.Combine("rules").FullPath}:/rules:ro")
            .AppendSwitch("--workdir", workdir)
            .Append("--entrypoint promtool")
            .Append(PromtoolImage);

        context.Run("docker", Promtool("/rules").Append("check rules").Append(string.Join(' ', rules)));
        context.Run("docker", Promtool("/rules/tests").Append("test rules").Append(string.Join(' ', tests)));
    }

    private static void CheckDashboards(BuildContext context, DirectoryPath chart)
    {
        var uids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in context.GetFiles(chart.FullPath + "/dashboards/*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file.FullPath));
            var root = document.RootElement;
            var uid = root.GetProperty("uid").GetString() ?? string.Empty;
            if (!uids.Add(uid))
            {
                throw new CakeException($"{file.GetFilename()}: dashboard uid '{uid}' is used twice.");
            }

            // Datasources are provisioned with fixed uids; a dashboard exported with a random one would show
            // "datasource not found" on every other Grafana.
            var unknown = Datasources(root).Where(d => !KnownDatasources.Contains(d)).Distinct().ToList();
            if (unknown.Count > 0)
            {
                throw new CakeException($"{file.GetFilename()}: unknown datasource uid(s) {string.Join(", ", unknown)}.");
            }

            context.Log.Information($"Dashboard {uid}: {root.GetProperty("panels").GetArrayLength()} panels");
        }
    }

    private static void RenderChart(BuildContext context, DirectoryPath chart)
    {
        // Chart repositories are registered in a throwaway Helm configuration, not in the user's.
        var home = context.MakeAbsolute(context.Artifacts.Combine("helm-home"));
        var isolated = $"--repository-config \"{home.CombineWithFilePath("repositories.yaml").FullPath}\" --repository-cache \"{home.Combine("cache").FullPath}\"";

        foreach (var line in File.ReadAllLines(chart.CombineWithFilePath("Chart.yaml").FullPath))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("repository: https://", StringComparison.Ordinal))
            {
                var url = trimmed["repository: ".Length..];
                context.Run("helm", $"repo add {new Uri(url).Host.Split('.')[0]} {url} --force-update {isolated}");
            }
        }

        context.Run("helm", $"dependency build \"{chart.FullPath}\" {isolated}");
        context.Run("helm", $"lint \"{chart.FullPath}\"");
        context.Run("helm", new ProcessArgumentBuilder()
            .Append("template observability").AppendQuoted(chart.FullPath)
            .Append("--namespace observability")
            .AppendSwitchQuoted("--output-dir", context.MakeAbsolute(context.Artifacts.Combine("observability")).FullPath));
    }

    private static IEnumerable<string> Datasources(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("datasource") && property.Value.ValueKind == JsonValueKind.Object
                    && property.Value.TryGetProperty("uid", out var uid))
                {
                    yield return uid.GetString() ?? string.Empty;
                }

                foreach (var nested in Datasources(property.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray().SelectMany(Datasources))
            {
                yield return item;
            }
        }
    }
}
