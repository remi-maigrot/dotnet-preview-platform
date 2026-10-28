namespace Devctl.Cli.Infrastructure;

/// <summary>Finds the repository root (the directory holding PreviewPlatform.slnx) from the current directory.</summary>
internal sealed class RepositoryLocator(string startDirectory)
{
    public const string Marker = "PreviewPlatform.slnx";

    public string? FindRoot()
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, Marker)))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
