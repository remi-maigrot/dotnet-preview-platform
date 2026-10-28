using System.Text.RegularExpressions;

namespace Devctl.Cli.Diagnostics;

/// <summary>
/// Removes secrets and email addresses from text that may leave the machine. It errs on the side of removing
/// too much: a diagnosis survives a redacted value, a leaked password does not.
/// </summary>
internal static partial class Redactor
{
    public const string Placeholder = "<redacted>";

    public static string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        text = AgeKey().Replace(text, Placeholder);
        text = Jwt().Replace(text, Placeholder);
        text = Bearer().Replace(text, m => $"{m.Groups["scheme"].Value} {Placeholder}");
        text = UrlCredentials().Replace(text, m => $"{m.Groups["scheme"].Value}{m.Groups["user"].Value}:{Placeholder}@");
        text = SecretSetting().Replace(text, m => $"{m.Groups["key"].Value}{m.Groups["separator"].Value}{Placeholder}");
        return Email().Replace(text, "<email>");
    }

    [GeneratedRegex(@"AGE-SECRET-KEY-1[0-9A-Z]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AgeKey();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Jwt();

    [GeneratedRegex(@"(?<scheme>Bearer|Basic)\s+[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Bearer();

    [GeneratedRegex(@"(?<scheme>[a-z][a-z0-9+.-]*://)(?<user>[^:/@\s]+):[^@\s]+@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UrlCredentials();

    // key=value, key: value, "key": "value" for any key that names a secret, as in connection strings,
    // environment variables and JSON. The value stops at a separator, a quote or a space.
    [GeneratedRegex(
        @"(?<key>[\w.-]*(?:password|passwd|pwd|secret|token|signingkey|api[_-]?key|access[_-]?key|private[_-]?key)[\w.-]*""?)(?<separator>\s*[:=]\s*""?)[^;""'\s,}]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SecretSetting();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Email();
}
