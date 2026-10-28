using Cake.Common;
using Cake.Common.IO;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Frosting;

namespace Build.Tasks;

[TaskName("Helm-Lint")]
[TaskDescription("helm lint, then renders the chart with every ci/*.yaml values file and validates it with kubeconform")]
public sealed class HelmLintTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        var chart = context.ChartDirectory.FullPath;
        var rendered = context.Artifacts.Combine("helm");
        context.EnsureDirectoryExists(rendered);

        foreach (var values in context.GetFiles(chart + "/ci/*.yaml"))
        {
            var name = values.GetFilenameWithoutExtension().FullPath;
            context.Log.Information($"Chart with {name}");
            context.Run("helm", new ProcessArgumentBuilder().Append("lint --strict").AppendQuoted(chart).AppendSwitchQuoted("--values", values.FullPath));

            var exitCode = context.StartProcess("helm", new ProcessSettings
            {
                Arguments = new ProcessArgumentBuilder()
                    .Append("template sample-api").AppendQuoted(chart)
                    .AppendSwitchQuoted("--values", values.FullPath)
                    .AppendSwitchQuoted("--output-dir", rendered.Combine(name).FullPath),
            });
            if (exitCode != 0)
            {
                throw new CakeException($"helm template failed for {name}");
            }

            context.Run("kubeconform", new ProcessArgumentBuilder()
                .Append("-strict -summary -kubernetes-version 1.31.0")
                .AppendQuoted(rendered.Combine(name).FullPath));
        }
    }
}
