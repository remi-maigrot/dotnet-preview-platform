using Cake.Common.IO;
using Cake.Core;
using Cake.Core.IO;

namespace Build;

internal static class CoverageReport
{
    /// <summary>Merges the Cobertura files of every test project into one HTML, Markdown and Cobertura report.</summary>
    public static void Generate(BuildContext context)
    {
        var reports = context.GetFiles(context.TestResults.FullPath + "/**/coverage.cobertura*.xml");
        if (reports.Count == 0)
        {
            throw new InvalidOperationException("No coverage report was produced.");
        }

        context.CleanDirectory(context.CoverageReport);
        var arguments = new ProcessArgumentBuilder()
            .Append("reportgenerator")
            .AppendQuoted($"-reports:{string.Join(';', reports.Select(r => r.FullPath))}")
            .AppendQuoted($"-targetdir:{context.CoverageReport.FullPath}")
            .Append("-reporttypes:Html;Cobertura;MarkdownSummaryGithub;JsonSummary")
            .Append("-verbosity:Warning");
        context.Run("dotnet", arguments);
    }

    public static FilePath MergedCobertura(BuildContext context) => context.CoverageReport.CombineWithFilePath("Cobertura.xml");
}
