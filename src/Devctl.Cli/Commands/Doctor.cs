using System.Text.RegularExpressions;
using Devctl.Cli.Infrastructure;

namespace Devctl.Cli.Commands;

internal enum ToolStatus
{
    Ok,
    Outdated,
    Missing,
    NotRunning,
}

internal sealed record ToolCheck(string Name, string Minimum, string? Found, ToolStatus Status, bool Required, string? Hint)
{
    public bool IsBlocking => Required && Status != ToolStatus.Ok;
}

internal sealed record ToolRequirement(
    string Name,
    IReadOnlyList<string> VersionArguments,
    Version Minimum,
    bool Required,
    string InstallHint,
    string Purpose);

/// <summary>Checks the tools devctl drives. Required ones block <c>up</c>; optional ones only unlock extra tasks.</summary>
internal static partial class Doctor
{
    public static readonly IReadOnlyList<ToolRequirement> Requirements =
    [
        new("docker", ["version", "--format", "{{.Server.Version}}"], new Version(24, 0), true,
            "Install Docker Desktop, OrbStack or Colima, then start it.", "runs the k3d cluster and builds images"),
        new("k3d", ["version"], new Version(5, 6), true,
            "brew install k3d  (or see https://k3d.io)", "creates the local cluster and registry"),
        new("kubectl", ["version", "--client"], new Version(1, 30), true,
            "brew install kubectl", "applies manifests and streams logs"),
        new("helm", ["version", "--short"], new Version(3, 14), true,
            "brew install helm", "installs the observability stack"),
        new("trivy", ["--version"], new Version(0, 50), false,
            "brew install trivy", "image and IaC scans (Cake target Scan)"),
        new("kubeconform", ["-v"], new Version(0, 6), false,
            "brew install kubeconform", "manifest validation (Cake target Helm-Lint)"),
        new("k6", ["version"], new Version(0, 50), false,
            "brew install k6", "load tests in load/"),
    ];

    public static async Task<IReadOnlyList<ToolCheck>> CheckAsync(IProcessRunner runner, CancellationToken cancellationToken)
    {
        var checks = await Task.WhenAll(Requirements.Select(r => CheckAsync(runner, r, cancellationToken)));
        return checks;
    }

    internal static Version? ParseVersion(string output)
    {
        var match = VersionPattern().Match(output);
        if (!match.Success)
        {
            return null;
        }

        var patch = match.Groups[3].Success ? int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
        return new Version(
            int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
            patch);
    }

    private static async Task<ToolCheck> CheckAsync(IProcessRunner runner, ToolRequirement requirement, CancellationToken cancellationToken)
    {
        var minimum = $">= {requirement.Minimum}";
        var result = await runner.RunAsync(requirement.Name, requirement.VersionArguments, cancellationToken);
        if (result.ExitCode == CliWrapProcessRunner.NotInstalled)
        {
            return new ToolCheck(requirement.Name, minimum, null, ToolStatus.Missing, requirement.Required, requirement.InstallHint);
        }

        if (!result.Succeeded)
        {
            // docker answers "version" but fails when the daemon is down: installed, not usable.
            return new ToolCheck(requirement.Name, minimum, ParseVersion(result.StandardOutput + result.StandardError)?.ToString(),
                ToolStatus.NotRunning, requirement.Required, $"{requirement.Name} is installed but not responding. {requirement.InstallHint}");
        }

        var version = ParseVersion(result.StandardOutput);
        return version is null || version < requirement.Minimum
            ? new ToolCheck(requirement.Name, minimum, version?.ToString() ?? "unknown", ToolStatus.Outdated, requirement.Required,
                $"Upgrade {requirement.Name}: {requirement.InstallHint}")
            : new ToolCheck(requirement.Name, minimum, version.ToString(), ToolStatus.Ok, requirement.Required, null);
    }

    [GeneratedRegex(@"v?(\d+)\.(\d+)(?:\.(\d+))?", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex VersionPattern();
}
