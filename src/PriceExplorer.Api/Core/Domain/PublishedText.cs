using System.Text.RegularExpressions;

namespace PriceExplorer.Api.Core.Domain;

/// <summary>
/// Which code text published in the plan's files the app may keep and show. We have no license for the AMA's CPT
/// descriptions (CPT codes: five digits, or four and a letter) or the ADA's CDT ones (D and four digits); a plan's license
/// to publish them doesn't extend to us. So their names, descriptions and item names are never stored or returned,
/// whatever code type a file gives them. HCPCS Level II codes (a letter, A–C or E–V, and four digits) are CMS's and public
/// domain, so their text is kept. Revenue codes show only our own labels (revenue_codes.tsv), never a file's item names.
/// The SQL that reads saved names applies the same test (see CodeDescriptions.sql).
/// </summary>
internal static partial class PublishedText
{
    /// <summary>The file's name, description and item names for this code may be kept and shown.</summary>
    public static bool MayKeep(string? code) => code is not null && HcpcsLevelTwo().IsMatch(code);

    /// <summary>A CPT or CDT code: shown only with our own plain names or CMS's RBCS name, never the file's text.</summary>
    public static bool IsLicensed(string? code) => code is not null && CptOrCdt().IsMatch(code);

    [GeneratedRegex("^[A-CE-V][0-9]{4}$", RegexOptions.IgnoreCase)]
    private static partial Regex HcpcsLevelTwo();

    [GeneratedRegex("^([0-9]{4}[0-9A-Z]|D[0-9]{4})$", RegexOptions.IgnoreCase)]
    private static partial Regex CptOrCdt();
}
