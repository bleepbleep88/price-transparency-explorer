using System.Collections.Frozen;

namespace PriceExplorer.Api.Core.Domain;

/// <summary>National Provider Identifier rules (CMS NPI check-digit standard).</summary>
internal static class Npi
{
    /// <summary>
    /// 10 digits, starting with 1 or 2, ending in a Luhn check digit computed as if the card-issuer prefix 80840
    /// were present. The constant 24 accounts for that prefix.
    /// </summary>
    public static bool IsValid(long npi)
    {
        if (!HasStandardPrefix(npi))
        {
            return false;
        }

        var checkDigit = (int)(npi % 10);
        var body = npi / 10;
        var sum = 24;
        var doubleIt = true;
        while (body > 0)
        {
            var digit = (int)(body % 10);
            body /= 10;
            if (doubleIt)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleIt = !doubleIt;
        }

        return (10 - (sum % 10)) % 10 == checkDigit;
    }

    /// <summary>10 digits starting with 1 or 2, as every issued NPI is.</summary>
    public static bool HasStandardPrefix(long npi) => npi is >= 1_000_000_000L and <= 2_999_999_999L;

    /// <summary>
    /// Parses user input ("1396747861", "1396-747-861"). Returns null unless exactly 10 digits remain. A leading zero is
    /// kept as typed when shown back (see SearchContext.NpiText): one file publishes the 9-digit NPI 124507857.
    /// </summary>
    public static long? Parse(string? input)
    {
        var digits = Digits(input);
        return digits.Length == 10 && long.TryParse(digits, out var npi) ? npi : null;
    }

    internal static string Digits(string? input) =>
        input is null ? "" : string.Concat(input.Where(char.IsAsciiDigit));
}

/// <summary>Employer Identification Number rules. EINs have no check digit; only shape and IRS prefix can be checked.</summary>
internal static class Ein
{
    // Prefixes the IRS has never assigned (IRS "How EINs are assigned and valid EIN prefixes").
    private static readonly FrozenSet<string> NeverIssuedPrefixes =
        new[] { "00", "07", "08", "09", "17", "18", "19", "28", "29", "49", "69", "70", "78", "79", "89", "96", "97" }
            .ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Returns the 9 digits ("113471596") or null when the value isn't 9 digits after removing punctuation.</summary>
    public static string? Normalize(string? value)
    {
        var digits = Npi.Digits(value);
        return digits.Length == 9 ? digits : null;
    }

    public static string Format(string nineDigits) => $"{nineDigits[..2]}-{nineDigits[2..]}";

    /// <summary>
    /// True for prefixes the IRS never issues. In these files they sit almost entirely on solo practitioners and may
    /// be Social Security numbers published by mistake, so the UI masks them.
    /// </summary>
    public static bool HasNeverIssuedPrefix(string nineDigits) => NeverIssuedPrefixes.Contains(nineDigits[..2]);

    public static string Mask(string nineDigits) => $"••-•••{nineDigits[^4..]}";
}
