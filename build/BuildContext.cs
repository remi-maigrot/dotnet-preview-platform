using Cake.Common;
using Cake.Common.Build;
using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;

namespace Build;

/// <summary>Everything a task needs to know about the current build. Every value can be overridden from the command line.</summary>
public sealed class BuildContext : FrostingContext
{
    public BuildContext(ICakeContext context)
        : base(context)
    {
        BuildConfiguration = context.Argument("configuration", "Release");
        IsCi = context.BuildSystem().IsRunningOnGitHubActions;
        GitSha = context.Argument("sha", context.EnvironmentVariable("GITHUB_SHA") ?? this.Git("rev-parse HEAD"));
        Version = context.Argument("version", $"0.0.0-{GitSha[..7]}");
        ContainerRegistry = context.Argument("registry", "ghcr.io/remi-maigrot");
        ImageTag = context.Argument("image-tag", GitSha);
        Push = context.Argument("push", false);
        Platforms = context.Argument("platforms", string.Empty);
        SkipIntegrationTests = context.Argument("skip-integration-tests", false);
        CoverageThreshold = context.Argument("coverage-threshold", 80d);

        Solution = new FilePath("PreviewPlatform.slnx");
        Artifacts = new DirectoryPath("artifacts");
        ChartDirectory = new DirectoryPath("deploy/helm/sample-api");
    }

    public string BuildConfiguration { get; }

    public bool IsCi { get; }

    public string GitSha { get; }

    /// <summary>SemVer of the build: the tag on a release, 0.0.0-&lt;sha&gt; otherwise.</summary>
    public string Version { get; }

    public string ContainerRegistry { get; }

    public string ImageTag { get; }

    /// <summary>Push images while building them (CI only, requires a registry login).</summary>
    public bool Push { get; }

    /// <summary>Comma-separated target platforms for images. Empty builds for the local platform only.</summary>
    public string Platforms { get; }

    /// <summary>Skips tests that need Docker (Testcontainers), for runners without a Linux Docker engine.</summary>
    public bool SkipIntegrationTests { get; }

    /// <summary>Minimum line coverage, in percent, of the domain and application layers.</summary>
    public double CoverageThreshold { get; }

    public FilePath Solution { get; }

    public DirectoryPath Artifacts { get; }

    public DirectoryPath TestResults => Artifacts.Combine("test-results");

    public DirectoryPath CoverageReport => Artifacts.Combine("coverage");

    public DirectoryPath SbomDirectory => Artifacts.Combine("sbom");

    public DirectoryPath ScanDirectory => Artifacts.Combine("scan");

    public DirectoryPath PackagesDirectory => Artifacts.Combine("packages");

    public DirectoryPath ChartDirectory { get; }

    /// <summary>Container images built from this repository.</summary>
    public IReadOnlyList<ContainerImage> Images { get; } =
    [
        new("sample-api", "src/Sample.Api/Dockerfile"),
        new("sample-migrator", "src/Sample.Migrator/Dockerfile"),
        new("preview-operator", "src/Preview.Operator/Dockerfile"),
        new("sample-deployer", "deploy/helm/Dockerfile"),
    ];

    public string ImageReference(ContainerImage image) => $"{ContainerRegistry}/{image.Name}:{ImageTag}";
}

public sealed record ContainerImage(string Name, string Dockerfile);
