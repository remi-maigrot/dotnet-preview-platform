using System.Text.RegularExpressions;
using Devctl.Cli.Infrastructure;

namespace Devctl.Cli.Commands;

/// <summary>
/// Installs deploy/observability (collector, Prometheus, Alertmanager, Grafana, Tempo, Loki, dashboards and alert
/// rules) as one Helm release. Running it again upgrades the release in place.
/// </summary>
internal sealed partial class ObservabilityStack(DevctlContext context, Output output)
{
    public const string ReleaseName = "observability";

    public async Task<string> InstallAsync(string root, string kubeContext, CancellationToken cancellationToken)
    {
        var chart = Path.Combine(root, "deploy", "observability");
        var manifest = Path.Combine(chart, "Chart.yaml");
        if (!File.Exists(manifest))
        {
            throw new StepFailedException($"{manifest} not found.", "Run devctl from an up-to-date clone of the repository.");
        }

        // The chart repositories go into a Helm configuration of our own under artifacts/: the user's
        // `helm repo list` stays as it was.
        var helmHome = Path.Combine(root, "artifacts", "devctl", "helm");
        string[] isolated =
        [
            "--repository-config", Path.Combine(helmHome, "repositories.yaml"),
            "--repository-cache", Path.Combine(helmHome, "cache"),
        ];

        foreach (var url in RepositoriesOf(await File.ReadAllTextAsync(manifest, cancellationToken)))
        {
            await context.RunAsync(output, "helm", ["repo", "add", new Uri(url).Host.Split('.')[0], url, "--force-update", .. isolated], cancellationToken);
        }

        await context.RunAsync(output, "helm", ["dependency", "build", chart, .. isolated], cancellationToken);

        // First install pulls about 1.5 GB of images: the timeout is generous on purpose.
        await context.RunAsync(output, "helm",
        [
            "upgrade", "--install", ReleaseName, chart,
            "--kube-context", kubeContext,
            "--namespace", Platform.ObservabilityNamespace, "--create-namespace",
            "--wait", "--timeout", "15m",
        ], cancellationToken);
        return "prometheus, grafana, tempo, loki";
    }

    internal static IReadOnlyList<string> RepositoriesOf(string chartYaml) =>
        [.. Repository().Matches(chartYaml).Select(m => m.Groups["url"].Value).Distinct(StringComparer.Ordinal)];

    [GeneratedRegex(@"^\s*repository:\s*(?<url>https://\S+)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Repository();
}
