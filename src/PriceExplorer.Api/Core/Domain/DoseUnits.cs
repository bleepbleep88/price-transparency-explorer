using System.Globalization;
using System.Text.RegularExpressions;
using PriceExplorer.Api.Core.Models.Rates;

namespace PriceExplorer.Api.Core.Domain;

/// <summary>
/// Reads how much one HCPCS unit holds from a drug code's name: "Injection, propofol, 10 mg" → 10 mg; "per ml" → 1 ml;
/// "Infusion, normal saline solution, 250 cc" → 250 ml; "per 100 units" → 100 units. A unit without a number counts only
/// after "per" or "up to", and "mg/ml" is a concentration, not an amount. A name with two or more amounts names no single
/// unit ("betamethasone acetate 3 mg and betamethasone sodium phosphate 3 mg"), so it gets none, with two exceptions: a
/// total in parentheses ("1 gram/0.125 grams (1.125 grams)", "300 micrograms (1500 i.u.)") and "(500 ml = 1 unit)".
/// Pure; covered by unit tests.
/// </summary>
internal static partial class DoseUnits
{
    public static DoseUnit? Parse(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (OneUnitIs().Match(name) is { Success: true } equals)
        {
            return Unit(equals.Groups["number"].Value, equals.Groups["unit"].Value);
        }

        var amounts = new List<Match>();
        foreach (Match match in Amount().Matches(name))
        {
            // A unit with neither a number nor "per" is a word ending (no "mg" in "img"), or a unit of something else.
            var end = match.Index + match.Length;
            if ((match.Groups["number"].Length == 0 && !match.Groups["per"].Success) || (end < name.Length && name[end] == '/'))
            {
                continue;
            }

            amounts.Add(match);
        }

        if (amounts.Count == 1)
        {
            return Unit(amounts[0].Groups["number"].Value, amounts[0].Groups["unit"].Value);
        }

        // Several amounts: only a total restated alone in parentheses, last, says what one unit holds.
        var last = amounts.LastOrDefault();
        return last is not null && Total().Match(name) is { Success: true } total && total.Index <= last.Index && last.Index + last.Length <= total.Index + total.Length
            ? Unit(last.Groups["number"].Value, last.Groups["unit"].Value)
            : null;
    }

    private static DoseUnit? Unit(string number, string written)
    {
        var amount = number.Length == 0 ? 1m : decimal.Parse(number.Replace(",", "", StringComparison.Ordinal), NumberStyles.Number, CultureInfo.InvariantCulture);
        if (amount <= 0)
        {
            return null;
        }

        var unit = UnitOf(written);
        var shown = amount.ToString("#,0.####", CultureInfo.InvariantCulture);
        return new DoseUnit(amount, unit, unit == "unit" ? $"{shown} {(amount == 1 ? "unit" : "units")}" : $"{shown} {unit}");
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

    private const string Number = @"(?<number>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?|\.\d+)";
    private const string Units = @"(?<unit>mg\b|mcg\b|micrograms?\b|gm\b|grams?\b|g\b|ml\b|cc\b|meq\b|units?\b|i\.u\.|iu\b)";

    [GeneratedRegex(@"(?<per>\b(?:per|up to)\s+)?" + Number + @"?\s*" + Units, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Amount();

    /// <summary>"(500 ml = 1 unit)": one unit is the amount before the equals sign.</summary>
    [GeneratedRegex(@"\(\s*" + Number + @"\s*" + Units + @"\s*=\s*1\s*units?\s*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OneUnitIs();

    /// <summary>An amount alone in parentheses at the end of the amounts: "(1.125 grams)", "(10 mg)", "(1500 i.u.)".</summary>
    [GeneratedRegex(@"\(\s*" + Number + @"\s*" + Units + @"\s*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.RightToLeft)]
    private static partial Regex Total();
}
