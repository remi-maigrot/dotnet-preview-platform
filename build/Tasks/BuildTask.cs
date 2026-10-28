using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Build;
using Cake.Common.Tools.DotNet.MSBuild;
using Cake.Frosting;

namespace Build.Tasks;

[TaskName("Build")]
[TaskDescription("Compiles the solution; any warning fails the build")]
[IsDependentOn(typeof(RestoreTask))]
public sealed class BuildTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context) =>
        context.DotNetBuild(context.Solution.FullPath, new DotNetBuildSettings
        {
            Configuration = context.BuildConfiguration,
            NoRestore = true,
            MSBuildSettings = new DotNetMSBuildSettings()
                .WithProperty("InformationalVersion", context.Version)
                .WithProperty("SourceRevisionId", context.GitSha),
        });
}
