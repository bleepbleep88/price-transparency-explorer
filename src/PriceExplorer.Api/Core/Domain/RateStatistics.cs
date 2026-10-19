namespace PriceExplorer.Api.Core.Domain;

/// <summary>Percentiles over one value per provider. Linear interpolation, same as DuckDB quantile_cont and numpy's default.</summary>
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
        return decimal.Round(sorted[lower] + (fraction * (sorted[upper] - sorted[lower])), 2);
    }

    public static decimal Median(IReadOnlyList<decimal> sorted) => Quantile(sorted, 0.5);

    /// <summary>Share of other providers priced below this value, 0–100.</summary>
    public static int PercentRank(IReadOnlyList<decimal> sorted, decimal value)
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

        return (int)Math.Round(100m * below / (sorted.Count - 1), MidpointRounding.AwayFromZero);
    }

    public static decimal? Ratio(decimal numerator, decimal? denominator) =>
        denominator is > 0 ? decimal.Round(numerator / denominator.Value, 2) : null;
}

/// <summary>Turns what a user types into the canonical codes stored in the database.</summary>
internal static class CodeInput
{
    /// <summary>Removes spaces and upper-cases. Returns "" when nothing usable is left.</summary>
    public static string Clean(string? input) =>
        string.Concat((input ?? "").Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

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
