using System.Globalization;
using System.Text.RegularExpressions;

namespace Preview.Operator.Reconciliation;

/// <summary>Parses the Go-style durations accepted by the CRD (<c>2h</c>, <c>90m</c>, <c>1h30m</c>).</summary>
internal static partial class Ttl
{
    public static bool TryParse(string? value, out TimeSpan ttl)
    {
        ttl = TimeSpan.Zero;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var match = DurationPattern().Match(value);
        if (!match.Success)
        {
            return false;
        }

        var hours = match.Groups["h"].Success ? int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture) : 0;
        var minutes = match.Groups["m"].Success ? int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        ttl = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
        return ttl > TimeSpan.Zero;
    }

    [GeneratedRegex(@"^(?:(?<h>\d{1,4})h)?(?:(?<m>\d{1,5})m)?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex DurationPattern();
}
