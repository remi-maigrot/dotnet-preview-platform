using Cake.Common.IO;
using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;

namespace Build.Tasks;

[TaskName("Publish")]
[TaskDescription("Pushes the Helm chart to the OCI registry (images are pushed by Docker-Build with --push=true)")]
[IsDependentOn(typeof(PackageTask))]
public sealed class PublishTask : FrostingTask<BuildContext>
{
    public override bool ShouldRun(BuildContext context) => context.Push;

    public override void Run(BuildContext context)
    {
        if (!context.IsCi)
        {
            throw new CakeException("Publish only runs in CI: artifacts are published from a clean, audited build.");
        }

        foreach (var chart in context.GetFiles(context.PackagesDirectory.FullPath + "/*.tgz"))
        {
            context.Run("helm", new ProcessArgumentBuilder()
                .Append("push").AppendQuoted(chart.FullPath).AppendQuoted($"oci://{context.ContainerRegistry}/charts"));
        }
    }
}
