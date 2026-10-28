using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Format;
using Cake.Frosting;

namespace Build.Tasks;

[TaskName("Format-Check")]
[TaskDescription("Fails if the code does not follow .editorconfig (whitespace, style and analyzer fixes)")]
[IsDependentOn(typeof(RestoreTask))]
public sealed class FormatCheckTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context) =>
        context.DotNetFormat(context.Solution.FullPath, new DotNetFormatSettings { VerifyNoChanges = true, NoRestore = true });
}
