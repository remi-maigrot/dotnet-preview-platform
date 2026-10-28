namespace Preview.Operator.Reconciliation;

/// <summary>An OCI image reference split into repository and tag or digest.</summary>
internal sealed record ImageReference(string Repository, string? Tag, string? Digest)
{
    public static bool TryParse(string? value, out ImageReference image)
    {
        image = new ImageReference(string.Empty, null, null);
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var at = value.IndexOf('@', StringComparison.Ordinal);
        if (at > 0)
        {
            image = new ImageReference(value[..at], null, value[(at + 1)..]);
            return image.Digest!.StartsWith("sha256:", StringComparison.Ordinal);
        }

        // The tag separator is the last ':' after the last '/', so registry ports (host:5000/...) are not mistaken for tags.
        var lastSlash = value.LastIndexOf('/');
        var colon = value.LastIndexOf(':');
        if (colon <= lastSlash)
        {
            return false;
        }

        image = new ImageReference(value[..colon], value[(colon + 1)..], null);
        return image.Tag!.Length > 0;
    }

    /// <summary>The image with the same registry path and version but another name (sample-api → sample-migrator).</summary>
    public ImageReference Sibling(string name)
    {
        var lastSlash = Repository.LastIndexOf('/');
        var prefix = lastSlash < 0 ? string.Empty : Repository[..(lastSlash + 1)];
        return this with { Repository = prefix + name };
    }

    public override string ToString() => Digest is null ? $"{Repository}:{Tag}" : $"{Repository}@{Digest}";
}
