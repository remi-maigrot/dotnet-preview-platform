using Cake.Common.IO;
using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;

namespace Build.Tasks;

[TaskName("Sbom")]
[TaskDescription("Generates CycloneDX SBOMs: NuGet dependency graph and full image contents")]
[IsDependentOn(typeof(RestoreTask))]
[IsDependentOn(typeof(DockerBuildTask))]
public sealed class SbomTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.EnsureDirectoryExists(context.SbomDirectory);

        context.Run("dotnet", new ProcessArgumentBuilder()
            .Append("CycloneDX")
            .AppendQuoted("src/Sample.Api/Sample.Api.csproj")
            .AppendSwitchQuoted("--output", context.SbomDirectory.FullPath)
            .AppendSwitchQuoted("--filename", "sample-api.nuget.cdx.json")
            .AppendSwitch("--output-format", "Json")
            .AppendSwitch("--set-version", context.Version));

        foreach (var image in context.Images)
        {
            context.Run("trivy", new ProcessArgumentBuilder()
                .Append("image --quiet --format cyclonedx")
                .AppendSwitchQuoted("--output", context.SbomDirectory.CombineWithFilePath($"{image.Name}.image.cdx.json").FullPath)
                .AppendQuoted(context.ImageReference(image)));
        }
    }
}
