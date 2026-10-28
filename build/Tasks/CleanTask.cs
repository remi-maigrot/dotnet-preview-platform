using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Frosting;

namespace Build.Tasks;

[TaskName("Clean")]
[TaskDescription("Deletes build outputs and artifacts")]
public sealed class CleanTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.CleanDirectory(context.Artifacts);
        context.DotNetClean(context.Solution.FullPath, new() { Configuration = context.BuildConfiguration, NoLogo = true });
    }
}
