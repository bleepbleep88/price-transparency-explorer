using System.Globalization;
using System.Text.RegularExpressions;
using PriceExplorer.Api.Core.Models.Rates;

namespace PriceExplorer.Api.Core.Domain;

/// <summary>
/// Reads how much one HCPCS unit holds from a drug code's name: "Injection, propofol, 10 mg" → 10 mg; "per ml" → 1 ml;
/// "Infusion, normal saline solution, 250 cc" → 250 ml; "per 100 units" → 100 units. The last amount in the name is
/// the billing unit ("Low osmolar contrast material, 300-399 mg/ml iodine concentration, per ml" bills per ml), and a
/// unit without a number counts only after "per". Pure; covered by unit tests.
/// </summary>
internal static partial class DoseUnits
{
    public static DoseUnit? Parse(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        DoseUnit? last = null;
        foreach (Match match in Amount().Matches(name))
        {
            var number = match.Groups["number"].Value;
            var per = match.Groups["per"].Success;
            // A unit with neither a number nor "per" is a word ending (no "mg" in "img"), or a unit of something else.
            if (number.Length == 0 && !per)
            {
                continue;
            }

            // "mg/ml" is a concentration, not a billing unit.
            var end = match.Index + match.Length;
            if (end < name.Length && name[end] == '/')
            {
                continue;
            }

            var amount = number.Length == 0 ? 1m : decimal.Parse(number.Replace(",", "", StringComparison.Ordinal), NumberStyles.Number, CultureInfo.InvariantCulture);
            if (amount <= 0)
            {
                continue;
            }

            last = new DoseUnit(amount, UnitOf(match.Groups["unit"].Value), match.Value.Trim());
        }

        return last;
    }

    private static string UnitOf(string written) => written.ToLowerInvariant().Replace(".", "", StringComparison.Ordinal) switch
    {
        "mcg" or "microgram" or "micrograms" => "mcg",
        "g" or "gm" or "gram" or "grams" => "g",
        "ml" or "cc" => "ml",
        "meq" => "meq",
        "mg" => "mg",
        _ => "unit",
    };

    [GeneratedRegex(
        @"(?<per>\b(?:per|up to)\s+)?(?<number>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?|\.\d+)?\s*(?<unit>mg\b|mcg\b|micrograms?\b|gm\b|grams?\b|g\b|ml\b|cc\b|meq\b|units?\b|i\.u\.|iu\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Amount();
}
