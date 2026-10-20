namespace PriceExplorer.Api.Core.Domain;

/// <summary>What completeness compares for one product in one snapshot: counts from its saved rate files.</summary>
/// <param name="ReadFully">Every rate file of the product was read to the end (none stopped partway).</param>
/// <param name="Eins">Distinct EINs in the files' provider groups.</param>
/// <param name="Codes">Distinct searchable codes among the rates.</param>
internal sealed record ProductCounts(int SnapshotId, DateOnly? IndexDate, string ProductKey, bool ReadFully, long Rates, long Eins, long Codes);

/// <summary>How complete one product's rate file looks next to the same product's nearby snapshots.</summary>
/// <param name="Score">The lowest of the three ratios. Null when no nearby snapshot has the product.</param>
/// <param name="Rates">Unique rates ÷ the nearby snapshots' median, rounded to 2 decimals.</param>
/// <param name="Eins">Distinct EINs ÷ the nearby snapshots' median.</param>
/// <param name="Codes">Distinct codes ÷ the nearby snapshots' median.</param>
/// <param name="ComparedWith">The nearby snapshots it was compared with (ids), oldest first.</param>
/// <param name="Partial">The score is below 0.8, or a file was read only in part. Partial files are left out of changes.</param>
public sealed record ProductCompleteness(
    decimal? Score, decimal? Rates, decimal? Eins, decimal? Codes, IReadOnlyList<int> ComparedWith, bool Partial);

/// <summary>
/// Payers sometimes publish part of a file: Centene's Ambetter file was 9 MB in December 2025 and 4 MB in March 2026,
/// against about 90 MB in other months. Compared with a full month, such a file reads as thousands of dropped rates. So
/// each product's file is measured against the same product in up to two snapshots before and two after: its unique
/// rates, EINs and codes, each divided by the nearby snapshots' median. Below 0.8 on any of them, it counts as partial.
/// </summary>
internal static class SnapshotCompleteness
{
    public const decimal PartialBelow = 0.8m;

    /// <summary>Nearby snapshots on each side. Files grow over the months, so only nearby ones are a fair yardstick.</summary>
    public const int NearbyEachSide = 2;

    public static IReadOnlyDictionary<(int SnapshotId, string ProductKey), ProductCompleteness> Score(IEnumerable<ProductCounts> counts)
    {
        var scores = new Dictionary<(int, string), ProductCompleteness>();
        foreach (var product in counts.GroupBy(count => count.ProductKey, StringComparer.Ordinal))
        {
            var ordered = product.OrderBy(count => count.IndexDate ?? DateOnly.MinValue).ThenBy(count => count.SnapshotId).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var self = ordered[i];
                var nearby = ordered
                    .Select((count, index) => (count, index))
                    .Where(pair => pair.index != i && Math.Abs(pair.index - i) <= NearbyEachSide)
                    .Select(pair => pair.count)
                    .ToList();

                decimal? rates = null, eins = null, codes = null, score = null;
                if (nearby.Count > 0)
                {
                    rates = Ratio(self.Rates, Median(nearby.Select(count => count.Rates)));
                    eins = Ratio(self.Eins, Median(nearby.Select(count => count.Eins)));
                    codes = Ratio(self.Codes, Median(nearby.Select(count => count.Codes)));
                    score = new[] { rates, eins, codes }.Min();
                }

                scores[(self.SnapshotId, self.ProductKey)] = new ProductCompleteness(
                    score, rates, eins, codes, nearby.Select(count => count.SnapshotId).ToList(),
                    Partial: !self.ReadFully || score < PartialBelow);
            }
        }

        return scores;
    }

    internal static decimal Median(IEnumerable<long> values)
    {
        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2m;
    }

    /// <summary>Rounded before it's compared with the threshold, so the flag always agrees with the number shown.</summary>
    private static decimal? Ratio(long value, decimal median) =>
        median > 0 ? decimal.Round(value / median, 2, MidpointRounding.AwayFromZero) : null;
}
