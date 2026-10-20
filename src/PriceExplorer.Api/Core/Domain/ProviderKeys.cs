using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PriceExplorer.Api.Core.Domain;

/// <summary>
/// A provider in results is identified by its tax ID: the EIN, or "type:value" for a tax ID that isn't an EIN.
/// An EIN with a prefix the IRS never issues may be a Social Security number published by mistake, so it gets an
/// opaque key instead: "u" + 12 hex characters of an HMAC-SHA256 of the number, under a secret made once per install
/// and kept in the database. Without that secret the key can't be turned back into the number.
/// What the key does not hide: results still show the last 4 digits (a display mask), the payer's public file holds
/// the full number (the source drawer links to it), and a search by the full number confirms a guess (decision 26).
/// The key is computed once, at ingestion, into provider_groups.provider_key, and every query reads that column.
/// </summary>
internal static partial class ProviderKeys
{
    /// <summary>A new per-install secret for masked keys.</summary>
    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(32);

    /// <summary>The key a provider group is identified by (stored in provider_groups.provider_key).</summary>
    public static string For(string? tinType, string? tinValue, string? ein, bool einUnverified, byte[] secret) =>
        ein is null ? $"{tinType}:{tinValue}"
        : einUnverified ? MaskedKey(ein, secret)
        : ein;

    /// <summary>"u" + the first 12 hex characters of HMAC-SHA256(secret, the 9 digits).</summary>
    public static string MaskedKey(string ein, byte[] secret) =>
        "u" + Convert.ToHexStringLower(HMACSHA256.HashData(secret, Encoding.ASCII.GetBytes(ein)))[..12];

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

    /// <summary>
    /// A provider key from a result row: a 9-digit EIN, an opaque "u…" key, or "type:value" for a tax ID that isn't an
    /// EIN. Null for blank or invalid input (validators reject invalid input first).
    /// </summary>
    public static string? NormalizeKey(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        if (input.Contains(':', StringComparison.Ordinal))
        {
            return input.Trim();
        }

        return TryNormalizeEinFilter(input, out var value) ? value : null;
    }

    [GeneratedRegex("^[uU][0-9a-fA-F]{12}$")]
    private static partial Regex OpaqueKey();
}
