using Cake.Common.IO;
using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;

namespace Build.Tasks;

[TaskName("Scan")]
[TaskDescription("Trivy: fails on any fixable critical CVE in the images and any high or critical IaC misconfiguration")]
[IsDependentOn(typeof(DockerBuildTask))]
public sealed class ScanTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.EnsureDirectoryExists(context.ScanDirectory);

        foreach (var image in context.Images)
        {
            var reference = context.ImageReference(image);
            // Full report kept as an artifact, whatever the severity.
            context.Run("trivy", new ProcessArgumentBuilder()
                .Append("image --quiet --format json")
                .AppendSwitchQuoted("--output", context.ScanDirectory.CombineWithFilePath($"{image.Name}.json").FullPath)
                .AppendQuoted(reference));
            // The gate itself: unfixed CVEs cannot be acted upon, so they are tracked but do not block.
            context.Run("trivy", new ProcessArgumentBuilder()
                .Append("image --quiet --severity CRITICAL --ignore-unfixed --exit-code 1")
                .AppendQuoted(reference));
        }

        context.Run("trivy", new ProcessArgumentBuilder()
            .Append("config --quiet --severity HIGH,CRITICAL --exit-code 1")
            .AppendSwitchQuoted("--ignorefile", ".trivyignore.yaml")
            // Upstream charts downloaded by helm dependency build: their findings belong to their projects.
            .AppendSwitchQuoted("--skip-dirs", "deploy/observability/charts")
            .AppendSwitchQuoted("--helm-values", context.ChartDirectory.CombineWithFilePath("ci/production-values.yaml").FullPath)
            .AppendQuoted("deploy"));
    }
}
