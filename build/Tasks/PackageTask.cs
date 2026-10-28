using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.MSBuild;
using Cake.Common.Tools.DotNet.Pack;
using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;

namespace Build.Tasks;

[TaskName("Package")]
[TaskDescription("Packs the devctl dotnet tool and the Helm chart, versioned with the build version")]
[IsDependentOn(typeof(BuildTask))]
public sealed class PackageTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.CleanDirectory(context.PackagesDirectory);

        foreach (var project in context.GetFiles("src/**/*.csproj").Where(IsPackable))
        {
            context.DotNetPack(project.FullPath, new DotNetPackSettings
            {
                Configuration = context.BuildConfiguration,
                NoBuild = true,
                NoRestore = true,
                OutputDirectory = context.PackagesDirectory,
                MSBuildSettings = new DotNetMSBuildSettings().WithProperty("PackageVersion", context.Version),
            });
        }

        context.Run("helm", new ProcessArgumentBuilder()
            .Append("package").AppendQuoted(context.ChartDirectory.FullPath)
            .AppendSwitch("--version", context.Version)
            .AppendSwitch("--app-version", context.ImageTag)
            .AppendSwitchQuoted("--destination", context.PackagesDirectory.FullPath));
    }

    private static bool IsPackable(FilePath project) =>
        File.ReadAllText(project.FullPath).Contains("<IsPackable>true</IsPackable>", StringComparison.Ordinal);
}
