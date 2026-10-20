namespace PriceExplorer.Api.Core.Domain;

/// <summary>
/// The arithmetic behind the insights that search doesn't already have: a provider's price index, and how two
/// products' rates for the same provider and service compare. Quantiles use <see cref="RateStatistics"/>, so they
/// follow the same rule as every group statistic (linear interpolation, rounded to cents).
/// </summary>
internal static class InsightMath
{
    /// <summary>A price index is shown only over at least this many groups, so one or two services don't decide it.</summary>
    public const int MinimumGroupsForIndex = 5;

    /// <summary>
    /// The median of a provider's × median values (one per group; null for groups too small to have a median), or null
    /// when fewer than <see cref="MinimumGroupsForIndex"/> groups have one. Taken over the values as shown, so anyone can
    /// check it from the scorecard's table.
    /// </summary>
    public static decimal? PriceIndex(IEnumerable<decimal?> vsMedians)
    {
        var ratios = vsMedians.OfType<decimal>().Order().ToList();
        return ratios.Count >= MinimumGroupsForIndex ? RateStatistics.Median(ratios) : null;
    }

    /// <summary>product ÷ versus, unrounded; null when versus isn't above zero.</summary>
    public static decimal? ExactRatio(decimal product, decimal versus) => versus > 0 ? product / versus : null;

    /// <summary>How far a ratio is from 1 in either direction: 0.5 and 2 are equally far (2); 1 isn't far at all (1).</summary>
    public static decimal Distance(decimal ratio) => ratio >= 1 ? ratio : 1 / ratio;

    /// <summary>The bands of product ÷ versus, lowest first. A band's edge belongs to the band further from 1.</summary>
    public static readonly IReadOnlyList<Band> Bands =
    [
        new("half-or-less", "Half or less", "lower"),
        new("lower-25-50", "25–50% lower", "lower"),
        new("lower-10-25", "10–25% lower", "lower"),
        new("lower-under-10", "Under 10% lower", "lower"),
        new("same", "Same rate", "same"),
        new("higher-under-10", "Under 10% higher", "higher"),
        new("higher-10-25", "10–25% higher", "higher"),
        new("higher-25-50", "25–50% higher", "higher"),
        new("higher-50-100", "50–100% higher", "higher"),
        new("double-or-more", "Double or more", "higher"),
    ];

    /// <summary>The key of the band an exact ratio falls in (see <see cref="Bands"/>).</summary>
    public static string BandOf(decimal ratio) => ratio switch
    {
        <= 0.5m => "half-or-less",
        <= 0.75m => "lower-25-50",
        <= 0.9m => "lower-10-25",
        < 1m => "lower-under-10",
        1m => "same",
        < 1.1m => "higher-under-10",
        < 1.25m => "higher-10-25",
        < 1.5m => "higher-25-50",
        < 2m => "higher-50-100",
        _ => "double-or-more",
    };

    /// <param name="Direction">"lower", "same" or "higher" than versus.</param>
    public sealed record Band(string Key, string Label, string Direction);
}
