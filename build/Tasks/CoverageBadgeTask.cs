using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Cake.Common.IO;
using Cake.Frosting;

namespace Build.Tasks;

[TaskName("Coverage-Badge")]
[TaskDescription("Writes a shields.io endpoint file with the domain + application coverage")]
public sealed class CoverageBadgeTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        var packages = XDocument.Load(CoverageReport.MergedCobertura(context).FullPath)
            .Descendants("package")
            .Where(p => (string)p.Attribute("name")! is "Sample.Domain" or "Sample.Application")
            .ToList();
        var covered = packages.Descendants("line").Count(l => (int)l.Attribute("hits")! > 0);
        var total = packages.Descendants("line").Count();
        var percent = total == 0 ? 0 : Math.Floor(covered * 1000d / total) / 10;

        var badge = new
        {
            schemaVersion = 1,
            label = "coverage",
            message = string.Create(CultureInfo.InvariantCulture, $"{percent:0.#}%"),
            color = percent >= 90 ? "brightgreen" : percent >= context.CoverageThreshold ? "green" : "red",
        };

        var directory = context.Artifacts.Combine("badges");
        context.EnsureDirectoryExists(directory);
        File.WriteAllText(directory.CombineWithFilePath("coverage.json").FullPath, JsonSerializer.Serialize(badge));
    }
}
