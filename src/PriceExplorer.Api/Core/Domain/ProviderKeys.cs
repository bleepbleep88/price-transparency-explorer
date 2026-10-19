using System.Text.RegularExpressions;

namespace PriceExplorer.Api.Core.Domain;

/// <summary>
/// A provider in results is identified by its EIN, except EINs with a prefix the IRS never issues (possibly SSNs):
/// those get an opaque key ("u" + 12 hex characters of SHA-256) so the full number never leaves the API.
/// The SQL computes the same key with DuckDB's sha256().
/// </summary>
internal static partial class ProviderKeys
{
    /// <summary>Accepts "11-3471596", "113471596", or an opaque key; returns the value to match on.</summary>
    public static bool TryNormalizeEinFilter(string? input, out string? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }

        var trimmed = input.Trim();
        if (OpaqueKey().IsMatch(trimmed))
        {
            value = trimmed.ToLowerInvariant();
            return true;
        }

        if (!trimmed.All(c => char.IsAsciiDigit(c) || c is '-' or ' '))
        {
            return false;
        }

        value = Ein.Normalize(trimmed);
        return value is not null;
    }

    [GeneratedRegex("^[uU][0-9a-fA-F]{12}$")]
    private static partial Regex OpaqueKey();
}
