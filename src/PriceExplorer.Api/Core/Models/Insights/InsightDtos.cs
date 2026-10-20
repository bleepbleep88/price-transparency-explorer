using PriceExplorer.Api.Core.Models.Rates;

namespace PriceExplorer.Api.Core.Models.Insights;

/// <summary>The saved statistics an insight reads: which snapshot, when they were built, and whether they're current.</summary>
/// <param name="BuiltAt">When the snapshot's statistics were built; null until the first build finishes.</param>
/// <param name="IsCurrent">False while they are missing or out of date (a reload or rebuild is under way, or a saved rate
/// has expired since): the numbers may not match a search until the rebuild finishes.</param>
public sealed record StatisticsBasis(SnapshotRef Snapshot, DateTimeOffset? BuiltAt, bool IsCurrent);

/// <summary>A category of service present in the results, with how many rows it has under the other filters.</summary>
public sealed record CategoryOption(string Key, string Label, int Count);

public sealed record ProductRef(string Key, string Label);

/// <summary>
/// Comparison groups with 10 or more providers, ranked by the gap to the lower quartile (median − 25th percentile) or by
/// spread (90th ÷ 10th percentile). The files have no claim counts, so a gap is per case (or per unit), never a total.
/// </summary>
/// <param name="MinimumProviders">Only groups with at least this many providers are ranked (full statistics).</param>
/// <param name="Categories">Categories with ranked groups under the other filters, most groups first.</param>
public sealed record GapLeaderboard(
    StatisticsBasis Statistics,
    GapLeaderboardEcho Query,
    int MinimumProviders,
    IReadOnlyList<CategoryOption> Categories,
    GapRowsPage Rows,
    IReadOnlyList<string> Notes);

/// <param name="MatchedCodes">The stored codes the code filter matched ("812" → 812 and 00812).</param>
public sealed record GapLeaderboardEcho(
    string? Product,
    string? Category,
    string? Setting,
    string? FeeType,
    string? Code,
    IReadOnlyList<string> MatchedCodes,
    string Sort,
    int Page,
    int PageSize);

public sealed record GapRowsPage(int Total, int Page, int PageSize, IReadOnlyList<GapRow> Items);

/// <summary>One comparison group on the leaderboard, with the statistics its search card shows.</summary>
/// <param name="Rank">Position in the whole ranking (1 = largest), not just this page.</param>
/// <param name="CodeName">The code's name as the plan's files publish it; for a revenue code, which the files never name,
/// the short label search shows ("Operating room"); otherwise null. Scorecard groups and pairs name codes the same way.</param>
/// <param name="LowerQuartileGap">Median − 25th percentile, per case or per unit (see <paramref name="Unit"/>).</param>
/// <param name="Search">Query parameters for GET /api/v1/rates (and the search page) that open this group.</param>
public sealed record GapRow(
    int Rank,
    string GroupKey,
    string ProductKey,
    string ProductLabel,
    string CodeType,
    string Code,
    string? CodeName,
    string CategoryKey,
    string CategoryLabel,
    string FeeType,
    string FeeTypeLabel,
    string SettingKey,
    string SettingLabel,
    IReadOnlyList<ModifierRef> Modifiers,
    string NegotiatedType,
    string Unit,
    bool PerUnit,
    int ProviderCount,
    RateStats Stats,
    decimal LowerQuartileGap,
    IReadOnlyDictionary<string, string> Search);

/// <summary>
/// One provider (tax ID) across every comparison group it has a rate in: each group's value, percentile and × median
/// (the numbers a search with this provider's EIN shows), and a price index summing them up.
/// </summary>
/// <param name="Highest">Up to 5 groups where its rate is furthest above the median, highest first.</param>
/// <param name="Lowest">Up to 5 groups where its rate is furthest below the median, lowest first.</param>
/// <param name="Groups">Every group it has a value in, by code.</param>
public sealed record ProviderScorecard(
    StatisticsBasis Statistics,
    ScorecardProvider Provider,
    PriceIndex Index,
    IReadOnlyList<ScorecardGroup> Highest,
    IReadOnlyList<ScorecardGroup> Lowest,
    IReadOnlyList<ScorecardGroup> Groups);

/// <param name="Ein">Full EIN, or null when masked (a prefix the IRS never issues) or when the tax ID isn't an EIN.</param>
/// <param name="EinDisplay">11-3471596, or ••-•••1234 when masked: never the full masked number.</param>
/// <param name="Name">The name on most of its groups; <paramref name="OtherNames"/> lists the rest.</param>
/// <param name="NpiCount">Distinct NPIs listed on its rates in the snapshot.</param>
/// <param name="WholeTaxIdContract">At least one of its rates comes from a contract with the whole tax ID (npi [0]).</param>
public sealed record ScorecardProvider(
    string ProviderKey,
    string? Ein,
    string EinDisplay,
    bool EinMasked,
    string Name,
    IReadOnlyList<string> OtherNames,
    int NpiCount,
    bool WholeTaxIdContract,
    IReadOnlyList<ProductGroups> Products);

public sealed record ProductGroups(string Key, string Label, int Groups);

/// <summary>
/// The provider's price index: the median, over its groups with a median (5+ providers), of its rate ÷ the group
/// median (each group's × median). 1.00 = typically at the median; 0.90 = typically 10% below.
/// </summary>
/// <param name="Value">Null when fewer than <paramref name="MinimumGroups"/> groups have a median.</param>
/// <param name="GroupsCounted">Groups with a median (5+ providers), the ones the index is taken over.</param>
/// <param name="Below">Counted groups where its rate is below the median; <paramref name="AtMedian"/> and
/// <paramref name="Above"/> likewise.</param>
public sealed record PriceIndex(
    decimal? Value,
    int GroupsCounted,
    int GroupsTotal,
    int MinimumGroups,
    int MinimumProviders,
    int Below,
    int AtMedian,
    int Above);

/// <summary>The provider in one comparison group: what a search for this code with its EIN shows for it.</summary>
/// <param name="Value">Its rate in the group (the NPI-weighted median of its rates there: always one it is paid).</param>
/// <param name="Percentile">Mid-rank among the other providers, as in search; null with fewer than 5 providers.</param>
/// <param name="VsMedian">Value ÷ the group median, as in search; null with fewer than 5 providers.</param>
/// <param name="Band">"low", "typical" or "high", as search rows say; null with fewer than 5 providers.</param>
/// <param name="Search">Query parameters for GET /api/v1/rates (and the search page) that show this provider in this group.</param>
public sealed record ScorecardGroup(
    string GroupKey,
    string ProductKey,
    string ProductLabel,
    string CodeType,
    string Code,
    string? CodeName,
    string CategoryKey,
    string CategoryLabel,
    string FeeType,
    string FeeTypeLabel,
    string SettingKey,
    string SettingLabel,
    IReadOnlyList<ModifierRef> Modifiers,
    string NegotiatedType,
    string Unit,
    bool PerUnit,
    int ProviderCount,
    string StatsLevel,
    decimal? Median,
    decimal Value,
    decimal RateLow,
    decimal RateHigh,
    int? Percentile,
    decimal? VsMedian,
    string? Band,
    IReadOnlyDictionary<string, string> Search);

/// <summary>
/// The same provider in the same comparison group under two products (everything in the group key but the product):
/// how <see cref="Product"/>'s rate compares with <see cref="Versus"/>'s, as the ratio product ÷ versus.
/// </summary>
/// <param name="Products">Every product in the snapshot, for choosing the two.</param>
/// <param name="Categories">Categories with pairs under the other filters, most pairs first.</param>
/// <param name="Distribution">How many pairs fall in each band of the ratio, lowest band first.</param>
/// <param name="ByCategory">The same summary per category of service, most pairs first.</param>
/// <param name="Pairs">The pairs, biggest difference first unless sorted otherwise.</param>
/// <param name="Notes">What was left out and why, in plain words.</param>
public sealed record ProductComparison(
    StatisticsBasis Statistics,
    ProductComparisonEcho Query,
    ProductRef Product,
    ProductRef Versus,
    IReadOnlyList<ProductRef> Products,
    IReadOnlyList<CategoryOption> Categories,
    ComparisonSummary Summary,
    IReadOnlyList<RatioBand> Distribution,
    IReadOnlyList<CategoryComparison> ByCategory,
    ComparisonPairsPage Pairs,
    IReadOnlyList<string> Notes);

public sealed record ProductComparisonEcho(
    string? Code,
    IReadOnlyList<string> MatchedCodes,
    string? Category,
    string? Setting,
    string Sort,
    int Page,
    int PageSize);

/// <summary>The pairs in numbers. Ratios are product ÷ versus; quantiles are linear-interpolation, like group statistics.</summary>
/// <param name="Identical">Pairs where both products pay exactly the same rate.</param>
/// <param name="Lower">Pairs where the product pays less than versus; <paramref name="Higher"/>, more.</param>
public sealed record ComparisonSummary(
    int Pairs,
    int Providers,
    int Codes,
    int Identical,
    int Lower,
    int Higher,
    decimal? P10,
    decimal? P25,
    decimal? Median,
    decimal? P75,
    decimal? P90);

/// <summary>A band of the ratio product ÷ versus.</summary>
/// <param name="Key">Stable identifier: "half-or-less", "lower-25-50", "lower-10-25", "lower-under-10", "same",
/// "higher-under-10", "higher-10-25", "higher-25-50", "higher-50-100", "double-or-more".</param>
/// <param name="Direction">"lower", "same" or "higher".</param>
public sealed record RatioBand(string Key, string Label, string Direction, int Count);

public sealed record CategoryComparison(string Key, string Label, int Pairs, int Identical, int Lower, int Higher, decimal Median);

public sealed record ComparisonPairsPage(int Total, int Page, int PageSize, IReadOnlyList<ComparisonPair> Items);

/// <param name="Ratio">ProductValue ÷ VersusValue, rounded to 2 decimals.</param>
/// <param name="Difference">ProductValue − VersusValue, in the group's unit.</param>
/// <param name="ProductSearch">Query parameters for the search that shows this provider in the product's group;
/// <paramref name="VersusSearch"/> likewise for versus.</param>
public sealed record ComparisonPair(
    string ProviderKey,
    string? Ein,
    string EinDisplay,
    bool EinMasked,
    string BusinessName,
    string CodeType,
    string Code,
    string? CodeName,
    string CategoryKey,
    string CategoryLabel,
    string FeeType,
    string FeeTypeLabel,
    string SettingKey,
    string SettingLabel,
    IReadOnlyList<ModifierRef> Modifiers,
    string Unit,
    bool PerUnit,
    decimal ProductValue,
    decimal VersusValue,
    decimal Ratio,
    decimal Difference,
    IReadOnlyDictionary<string, string> ProductSearch,
    IReadOnlyDictionary<string, string> VersusSearch);
