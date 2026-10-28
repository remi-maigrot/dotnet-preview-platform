using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Restore;
using Cake.Frosting;

namespace Build.Tasks;

[TaskName("Restore")]
[TaskDescription("Restores NuGet packages and local tools; lock files are enforced in CI")]
public sealed class RestoreTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.Run("dotnet", "tool restore");
        context.DotNetRestore(context.Solution.FullPath, new DotNetRestoreSettings { LockedMode = context.IsCi });
    }
}
