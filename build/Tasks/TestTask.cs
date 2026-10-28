using Cake.Common.IO;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Frosting;

namespace Build.Tasks;

[TaskName("Test")]
[TaskDescription("Runs every test project with coverage, then enforces the coverage threshold")]
[IsDependentOn(typeof(BuildTask))]
public sealed class TestTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.CleanDirectory(context.TestResults);

        var projects = context.GetFiles("tests/**/*.csproj")
            .Where(p => !p.GetFilenameWithoutExtension().FullPath.Equals("Smoke.Tests", StringComparison.Ordinal))
            .Where(p => !context.SkipIntegrationTests || !p.FullPath.Contains("IntegrationTests", StringComparison.Ordinal))
            .ToList();

        foreach (var project in projects)
        {
            var name = project.GetFilenameWithoutExtension().FullPath;
            context.Log.Information($"Testing {name}");
            // Microsoft.Testing.Platform mode (global.json): arguments after "--" go to the test application.
            context.Run("dotnet", new ProcessArgumentBuilder()
                .Append("test")
                .AppendSwitchQuoted("--project", project.FullPath)
                .AppendSwitch("--configuration", context.BuildConfiguration)
                .Append("--no-build")
                .AppendSwitchQuoted("--results-directory", context.TestResults.Combine(name).FullPath)
                .Append("--report-trx")
                .Append("--coverlet")
                .AppendSwitch("--coverlet-output-format", "cobertura")
                .AppendSwitchQuoted("--coverlet-include", "[Sample.*]*")
                .AppendSwitchQuoted("--coverlet-include", "[Preview.*]*")
                .AppendSwitchQuoted("--coverlet-include", "[Devctl.*]*")
                .AppendSwitchQuoted("--coverlet-exclude-by-file", "**/Migrations/*.cs"));
        }

        CoverageReport.Generate(context);
        if (!context.SkipIntegrationTests)
        {
            CoverageGate.Enforce(context, ["Sample.Domain", "Sample.Application"]);
        }
    }
}
