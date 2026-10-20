using PriceExplorer.Api.Core.Models.Rates;

namespace PriceExplorer.Api.Core.Models.History;

/// <summary>A snapshot the changes feed reads, and whether its saved statistics are current.</summary>
/// <param name="StatisticsBuiltAt">When its saved statistics were built; null before the first build finishes.</param>
/// <param name="StatisticsCurrent">
/// The saved statistics are what search shows now. False before the first build, while they're rebuilt (a rate file was
/// saved again or the app changed), after a failed build, or once a rate in them has expired: then the changes may be
/// out of date.
/// </param>
public sealed record HistorySnapshot(int SnapshotId, DateOnly? IndexDate, bool IsLatest, DateTimeOffset? StatisticsBuiltAt, bool StatisticsCurrent);

/// <summary>
/// Negotiated rates that differ between two snapshots' files, for one provider (tax ID) in one comparison group. A change
/// means the file says something different; it isn't a claim, and the files say nothing about how often a service is used.
/// </summary>
/// <param name="Current">Both snapshots' saved statistics are current. When false, say the changes may be out of date.</param>
/// <param name="Products">Every product in either snapshot, and whether it was compared.</param>
/// <param name="Counts">Counts under every filter except <c>kind</c> and paging.</param>
public sealed record ChangesResult(
    ChangesEcho Query,
    HistorySnapshot From,
    HistorySnapshot To,
    bool Current,
    IReadOnlyList<ChangesProduct> Products,
    ChangeCounts Counts,
    RateChangesPage Rows);

/// <summary>The request as the server ran it, with the snapshots it picked.</summary>
public sealed record ChangesEcho(
    int From, int To, string? Product, string? Code, string? CodeType, string? Kind, decimal? MinChangePct, bool? ListedElsewhere,
    string Sort, int Page, int PageSize);

/// <param name="Compared">Both snapshots have the product, and neither file looks partial: its rates are in the counts and rows.</param>
/// <param name="NotComparedBecause">Why it wasn't compared, in plain words; null when it was.</param>
public sealed record ChangesProduct(string ProductKey, string ProductLabel, bool Compared, string? NotComparedBecause);

/// <param name="Changed">Same provider and comparison group in both files with a different value (by at least the minimum
/// change, when one is set).</param>
/// <param name="Increased">Changed values that went up.</param>
/// <param name="Decreased">Changed values that went down.</param>
/// <param name="New">In the newer file only.</param>
/// <param name="NewListedElsewhere">Of the new ones, providers the older file lists for the same code under other
/// modifiers or another setting: a change in how the rate is listed more than a new contract.</param>
/// <param name="Dropped">In the older file only.</param>
/// <param name="DroppedListedElsewhere">Of the dropped ones, providers the newer file lists for the same code under other
/// modifiers or another setting.</param>
/// <param name="Unchanged">In both files with the same value.</param>
public sealed record ChangeCounts(
    int Changed, int Increased, int Decreased, int New, int NewListedElsewhere, int Dropped, int DroppedListedElsewhere, int Unchanged);

public sealed record RateChangesPage(int Total, int Page, int PageSize, IReadOnlyList<RateChange> Items);

/// <summary>One provider in one comparison group whose value differs between the files, or that is in only one of them.</summary>
/// <param name="Kind">changed | new | dropped.</param>
/// <param name="CodeName">The code's name as the plan's file publishes it, if it does.</param>
/// <param name="Unit">What one rate pays for, as the search card says.</param>
/// <param name="From">The provider's value in the older file: the rate search shows on its row (NPI-weighted median of its rates
/// in the group). Null when new.</param>
/// <param name="To">The provider's value in the newer file. Null when dropped.</param>
/// <param name="Change">To minus From, when changed.</param>
/// <param name="ChangePct">Change ÷ From × 100, rounded to 0.1. Null unless changed from an amount above zero.</param>
/// <param name="FromRateId">The older file's rate carrying From (for the source drawer). Null when new, or when that
/// snapshot's saved statistics aren't current (rate ids change when a file is saved again).</param>
/// <param name="ToRateId">The newer file's rate carrying To, on the same terms.</param>
/// <param name="FromRates">How many rates the provider has in the group in the older file. With more than one, its value
/// is their NPI-weighted median, which can move when a rate is added or removed.</param>
/// <param name="ToRates">How many rates it has in the group in the newer file.</param>
/// <param name="ListedElsewhere">New or dropped, but the other file lists the provider for the same code and fee type
/// under other modifiers or another setting.</param>
public sealed record RateChange(
    string Kind,
    string GroupKey,
    string ProductKey,
    string ProductLabel,
    string CodeType,
    string Code,
    string? CodeName,
    string FeeType,
    string FeeTypeLabel,
    string SettingKey,
    string SettingLabel,
    IReadOnlyList<ModifierRef> Modifiers,
    string NegotiatedType,
    string Unit,
    string ProviderKey,
    string? Ein,
    string EinDisplay,
    bool EinMasked,
    string BusinessName,
    decimal? From,
    decimal? To,
    decimal? Change,
    decimal? ChangePct,
    long? FromRateId,
    long? ToRateId,
    int? FromRates,
    int? ToRates,
    bool ListedElsewhere);

/// <summary>
/// A search's comparison groups across every loaded snapshot. Each point is what that snapshot's search shows for the
/// group (the search runs in each snapshot), so the newest point always equals the card.
/// </summary>
/// <param name="Selected">The snapshot whose search decided which groups are listed (the cards on screen).</param>
/// <param name="Snapshots">Every snapshot with saved rates, oldest first: the points a trend can have.</param>
public sealed record RateTrendResult(SnapshotRef Selected, IReadOnlyList<SnapshotRef> Snapshots, IReadOnlyList<GroupTrend> Groups);

/// <param name="Key">The comparison group key, as search returns it.</param>
/// <param name="Points">One per snapshot where the group has rates, oldest first.</param>
public sealed record GroupTrend(string Key, string ProductKey, IReadOnlyList<TrendPoint> Points);

/// <summary>A comparison group in one snapshot, as that snapshot's search card shows it.</summary>
/// <param name="Partial">The product's file in this snapshot looks partial next to nearby snapshots (see /api/v1/snapshots).</param>
/// <param name="StatsLevel">list (fewer than 5 providers: no median), basic (5–9) or full (10+).</param>
/// <param name="Provider">The searched provider's rate here, as the card marks it (the first highlighted provider); null
/// without an NPI, EIN or name filter, or when the provider has no rate in the group in this snapshot.</param>
public sealed record TrendPoint(
    int SnapshotId,
    DateOnly? IndexDate,
    bool Partial,
    int ProviderCount,
    string StatsLevel,
    decimal Min,
    decimal? P25,
    decimal? Median,
    decimal? P75,
    decimal Max,
    TrendProvider? Provider);

/// <param name="Rate">The provider's rate in the group: an NPI's own rate with an NPI filter, as its search row shows.</param>
public sealed record TrendProvider(string ProviderKey, string DisplayName, decimal Rate);
