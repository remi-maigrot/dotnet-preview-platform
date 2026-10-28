using Cake.Common;
using Cake.Core;
using Cake.Core.IO;

namespace Build;

/// <summary>Runs command-line tools (docker, helm, trivy, git) and fails the build on a non-zero exit code.</summary>
public static class Tools
{
    public static void Run(this ICakeContext context, string tool, ProcessArgumentBuilder arguments)
    {
        var exitCode = context.StartProcess(tool, new ProcessSettings { Arguments = arguments });
        if (exitCode != 0)
        {
            throw new CakeException($"'{tool} {arguments.RenderSafe()}' failed with exit code {exitCode}.");
        }
    }

    public static void Run(this ICakeContext context, string tool, string arguments) =>
        context.Run(tool, new ProcessArgumentBuilder().Append(arguments));

    public static string Git(this ICakeContext context, string arguments)
    {
        var exitCode = context.StartProcess(
            "git",
            new ProcessSettings { Arguments = arguments, RedirectStandardOutput = true },
            out var output);
        if (exitCode != 0)
        {
            throw new CakeException($"git {arguments} failed.");
        }

        return string.Join(string.Empty, output).Trim();
    }

    public static bool IsInstalled(this ICakeContext context, string tool) =>
        context.StartProcess(
            tool,
            new ProcessSettings { Arguments = "version", RedirectStandardOutput = true, RedirectStandardError = true }) == 0;
}
