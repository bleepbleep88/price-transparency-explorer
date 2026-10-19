using System.Text.RegularExpressions;

namespace PriceExplorer.Api.Core.Domain;

/// <summary>Billing code clean-up rules. Pure functions; covered by unit tests.</summary>
internal static partial class BillingCodes
{
    public const string Cpt = "CPT";
    public const string Hcpcs = "HCPCS";
    public const string RevenueCode = "RC";

    public static string NormalizeType(string? type) => (type ?? "").Trim().ToUpperInvariant();

    /// <summary>
    /// Trims, upper-cases, and restores leading zeros payers drop when they store codes as numbers:
    /// revenue code 490 is 0490, anesthesia CPT 812 is 00812.
    /// </summary>
    public static string Canonicalize(string type, string? raw)
    {
        var code = (raw ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0 || !IsAllDigits(code))
        {
            return code;
        }

        return type switch
        {
            Cpt when code.Length < 5 => code.PadLeft(5, '0'),
            RevenueCode when code.Length < 4 => code.PadLeft(4, '0'),
            "MS-DRG" or "APR-DRG" or "AP-DRG" when code.Length < 3 => code.PadLeft(3, '0'),
            _ => code,
        };
    }

    /// <summary>
    /// Reads a CPT or HCPCS code out of free text, e.g. the additional_information field Centene uses to say
    /// which procedure a revenue-code (facility) rate is for. Accepts only a whole-value code, never a fragment.
    /// </summary>
    public static bool TryParseProcedureCode(string? text, out string codeType, out string code)
    {
        var candidate = (text ?? "").Trim().ToUpperInvariant();
        if (ProcedureCodePattern().IsMatch(candidate))
        {
            code = candidate;
            codeType = char.IsAsciiLetter(candidate[0]) ? Hcpcs : Cpt;
            return true;
        }

        codeType = "";
        code = "";
        return false;
    }

    private static bool IsAllDigits(string value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    // 5 digits (CPT), 4 digits + letter (CPT Category II/III, PLA), or letter A-V + 4 digits (HCPCS Level II).
    [GeneratedRegex("^(?:[0-9]{5}|[0-9]{4}[A-Z]|[A-V][0-9]{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex ProcedureCodePattern();
}
