using System.Globalization;
using System.Xml.Linq;
using Cake.Core;
using Cake.Core.Diagnostics;

namespace Build;

/// <summary>Fails the build when the merged line coverage of a gated assembly is below the threshold.</summary>
internal static class CoverageGate
{
    public static void Enforce(BuildContext context, IReadOnlyCollection<string> assemblies)
    {
        var document = XDocument.Load(CoverageReport.MergedCobertura(context).FullPath);
        var rates = document.Descendants("package").ToDictionary(
            p => (string)p.Attribute("name")!,
            p => double.Parse((string)p.Attribute("line-rate")!, CultureInfo.InvariantCulture) * 100);

        var failures = new List<string>();
        foreach (var assembly in assemblies)
        {
            if (!rates.TryGetValue(assembly, out var rate))
            {
                failures.Add($"{assembly}: no coverage data");
                continue;
            }

            context.Log.Information($"Line coverage {assembly}: {rate:F1} % (threshold {context.CoverageThreshold} %)");
            if (rate < context.CoverageThreshold)
            {
                failures.Add($"{assembly}: {rate:F1} % < {context.CoverageThreshold} %");
            }
        }

        if (failures.Count > 0)
        {
            throw new CakeException("Coverage threshold not met: " + string.Join("; ", failures));
        }
    }
}
