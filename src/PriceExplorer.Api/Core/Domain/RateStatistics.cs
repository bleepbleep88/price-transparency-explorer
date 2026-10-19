using System.Text.RegularExpressions;

namespace PriceExplorer.Api.Core.Domain;

/// <summary>
/// Percentiles over one value per provider. Linear interpolation, same as DuckDB quantile_cont and numpy's default.
/// Results are rounded to cents half away from zero, like DuckDB round() and Excel ROUND, so a check in SQL matches.
/// </summary>
internal static class RateStatistics
{
    public const int MinimumForBasicStats = 5;
    public const int MinimumForFullStats = 10;

    public static string LevelFor(int providers) => providers switch
    {
        < MinimumForBasicStats => "list",
        < MinimumForFullStats => "basic",
        _ => "full",
    };

    /// <param name="sorted">Values in ascending order.</param>
    /// <param name="q">Quantile between 0 and 1.</param>
    public static decimal Quantile(IReadOnlyList<decimal> sorted, double q)
    {
        if (sorted.Count == 0)
        {
            throw new ArgumentException("Quantile of an empty list.", nameof(sorted));
        }

        var position = (sorted.Count - 1) * (decimal)q;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        var fraction = position - lower;
        return decimal.Round(sorted[lower] + (fraction * (sorted[upper] - sorted[lower])), 2, MidpointRounding.AwayFromZero);
    }

    public static decimal Median(IReadOnlyList<decimal> sorted) => Quantile(sorted, 0.5);

    /// <summary>
    /// The lower weighted median: the smallest value at which the running weight reaches half the total. It is always one
    /// of the given values, never a midpoint, so a provider with several rates is represented by one it is really paid.
    /// Weights below 1 count as 1 (a contract with the whole tax ID lists no NPIs).
    /// </summary>
    public static decimal WeightedMedian(IEnumerable<(decimal Value, int Weight)> items)
    {
        var ordered = items
            .GroupBy(item => item.Value)
            .Select(same => (Value: same.Key, Weight: same.Sum(item => (long)Math.Max(1, item.Weight))))
            .OrderBy(item => item.Value)
            .ToList();
        if (ordered.Count == 0)
        {
            throw new ArgumentException("Weighted median of an empty list.", nameof(items));
        }

        var total = ordered.Sum(item => item.Weight);
        var running = 0L;
        foreach (var item in ordered)
        {
            running += item.Weight;
            if (running * 2 >= total)
            {
                return item.Value;
            }
        }

        return ordered[^1].Value;
    }

    /// <summary>Share of other providers priced below this value, 0–100.</summary>
    public static int PercentRank(IReadOnlyList<decimal> sorted, decimal value) => PercentRank(sorted, value, value);

    /// <summary>
    /// Share of the other providers priced below <paramref name="value"/>, 0–100. <paramref name="own"/> is this provider's
    /// own entry in <paramref name="sorted"/>, which is left out (it can differ from <paramref name="value"/> when the row
    /// shows only the rates that matched a filter).
    /// </summary>
    public static int PercentRank(IReadOnlyList<decimal> sorted, decimal value, decimal own)
    {
        if (sorted.Count <= 1)
        {
            return 0;
        }

        var below = 0;
        foreach (var item in sorted)
        {
            if (item < value)
            {
                below++;
            }
        }

        if (own < value)
        {
            below--;
        }

        return (int)Math.Round(100m * below / (sorted.Count - 1), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// "low" (at or below the 25th percentile), "high" (at or above the 75th) or "typical". A value equal to the median is
    /// always typical, so in a fee schedule (25th percentile = median) the many providers paid the standard rate aren't "low".
    /// </summary>
    public static string Band(decimal value, decimal p25, decimal median, decimal p75) =>
        value <= p25 && value < median ? "low"
        : value >= p75 && value > median ? "high"
        : "typical";

    public static decimal? Ratio(decimal numerator, decimal? denominator) =>
        denominator is > 0 ? decimal.Round(numerator / denominator.Value, 2, MidpointRounding.AwayFromZero) : null;
}

/// <summary>Turns what a user types into the canonical codes stored in the database.</summary>
internal static partial class CodeInput
{
    /// <summary>Removes spaces and upper-cases. Returns "" when nothing usable is left.</summary>
    public static string Clean(string? input) =>
        string.Concat((input ?? "").Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    /// <summary>
    /// Splits what was typed into a code and an optional code type. A leading type word is understood ("CPT 99213",
    /// "rc 0490", "Revenue code 450", "HCPCS J0585"); a code type passed separately always wins over the word.
    /// </summary>
    public static (string Code, string? CodeType) Parse(string? input, string? codeType)
    {
        var explicitType = string.IsNullOrWhiteSpace(codeType) ? null : codeType.Trim().ToUpperInvariant();
        var match = TypeWord().Match((input ?? "").Trim());
        if (!match.Success)
        {
            return (Clean(input), explicitType);
        }

        var word = match.Groups["type"].Value.ToUpperInvariant();
        var typed = word.StartsWith("HCPC", StringComparison.Ordinal) ? BillingCodes.Hcpcs
            : word == "CPT" ? BillingCodes.Cpt
            : BillingCodes.RevenueCode;
        return (Clean(match.Groups["code"].Value), explicitType ?? typed);
    }

    // The code after the type word must contain a digit (every CPT, HCPCS and revenue code does), so a bare type word
    // like "HCPCS" or "Revenue" is never split into a fake code ("S", "ENUE"); it stays as typed and finds nothing.
    [GeneratedRegex(@"^(?<type>CPT|HCPCS?|RC|REV(?:ENUE)?(?:\s*CODE)?)\s*[:#-]?\s*(?<code>(?=[A-Za-z0-9\s]*[0-9])[A-Za-z0-9][A-Za-z0-9\s]*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TypeWord();

    /// <summary>
    /// Every stored code the input could mean. "490" could be revenue code 0490 or CPT 00490, and "812" is CPT 00812,
    /// so short numbers expand to each padded form. With a code type, only that type's form is used.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string cleaned, string? codeType)
    {
        if (codeType is not null)
        {
            return [BillingCodes.Canonicalize(codeType, cleaned)];
        }

        var candidates = new List<string> { cleaned };
        if (cleaned.Length is > 0 and < 5 && cleaned.All(char.IsAsciiDigit))
        {
            candidates.Add(cleaned.PadLeft(5, '0'));
            if (cleaned.Length < 4)
            {
                candidates.Add(cleaned.PadLeft(4, '0'));
            }
        }

        return candidates.Distinct(StringComparer.Ordinal).ToList();
    }
}
