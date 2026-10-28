using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;

namespace Build.Tasks;

[TaskName("Backup-Test")]
[TaskDescription("Backs up a seeded database with the backup image, restores it, and checks that a wrong key or an altered file fail")]
[IsDependentOn(typeof(DockerBuildTask))]
public sealed class BackupTestTask : FrostingTask<BuildContext>
{
    // Multi-platform builds are pushed, not loaded: the images are not in the local engine to run.
    public override bool ShouldRun(BuildContext context) => string.IsNullOrEmpty(context.Platforms);

    public override void Run(BuildContext context) =>
        context.Run("bash", new ProcessArgumentBuilder()
            .AppendQuoted("deploy/backup/test.sh")
            .AppendQuoted(context.ImageReference(context.Image("sample-backup")))
            .AppendQuoted(context.ImageReference(context.Image("sample-migrator")))
            .AppendQuoted(context.ImageReference(context.Image("sample-seeder"))));
}
