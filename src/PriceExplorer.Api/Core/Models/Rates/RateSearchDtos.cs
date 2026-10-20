using PriceExplorer.Api.Core.Domain;

namespace PriceExplorer.Api.Core.Models.Rates;

/// <summary>Search results for one billing code: fair comparison groups, the provider rows behind them, and hints.</summary>
public sealed record RateSearchResult(
    RateSearchEcho Query,
    SnapshotRef Snapshot,
    CodeInfo? Code,
    IReadOnlyList<RateGroup> Groups,
    RateRowsPage Rows,
    IReadOnlyList<SearchHint> Hints,
    SearchQuality Quality);

/// <summary>The search as the server understood it (normalized), so the UI can show exactly what ran.</summary>
public sealed record RateSearchEcho(
    string Code,
    IReadOnlyList<string> MatchedCodes,
    string? CodeType,
    string? Npi,
    string? Ein,
    string? Product,
    string? Setting,
    string? FeeType,
    string? Provider,
    string? Group,
    string Sort,
    int Page,
    int PageSize);

public sealed record SnapshotRef(int SnapshotId, DateOnly? IndexDate, bool IsLatest);

/// <param name="DescriptionSource">"payer_file" when the plan's file describes the code, otherwise "none".</param>
/// <param name="FoundVia">How the code appears in the files, e.g. "Billing code" or "Revenue code 0490 (Ambulatory surgery)".</param>
public sealed record CodeInfo(
    string CodeType,
    string Code,
    string? Name,
    string? Description,
    string DescriptionSource,
    ServiceCategory Category,
    string? LevelFamily,
    IReadOnlyList<string> FoundVia);

/// <summary>
/// A fair comparison group: same code, product, fee type, setting, modifiers and rate type. Stats count each provider (tax ID) once.
/// </summary>
/// <param name="StatsLevel">"list" (fewer than 5 providers: rates listed, no stats), "basic" (5–9: median and range), "full" (10+).</param>
/// <param name="MatchedProviders">Providers matching the NPI/EIN/name filter in this group; <c>Highlights</c> lists the first 5 by rate. 0 without a filter.</param>
/// <param name="LowerQuartileGap">Median minus 25th percentile: how far a typical (median) rate sits above the lower quartile. Plans and employers read it as room to steer care; providers as negotiating context. Full stats only.</param>
public sealed record RateGroup(
    string Key,
    string ProductKey,
    string ProductLabel,
    string CodeType,
    string Code,
    string FeeType,
    string FeeTypeLabel,
    string SettingKey,
    string SettingLabel,
    string? SettingNote,
    IReadOnlyList<PlaceRef> Places,
    IReadOnlyList<ModifierRef> Modifiers,
    string NegotiatedType,
    string Unit,
    bool PerUnit,
    IReadOnlyList<BilledAs> BilledAs,
    int ProviderCount,
    int RateCount,
    string StatsLevel,
    RateStats Stats,
    decimal? LowerQuartileGap,
    IReadOnlyList<decimal> ListedRates,
    IReadOnlyList<ProviderPosition> Highlights,
    int MatchedProviders,
    IReadOnlyList<string> Notes);

/// <param name="SpreadMultiple">Highest ÷ lowest. Only with 10+ providers; single outliers make it jumpy.</param>
/// <param name="P90ToP10">90th ÷ 10th percentile: a steadier measure of spread.</param>
public sealed record RateStats(
    decimal Min,
    decimal? P10,
    decimal? P25,
    decimal? Median,
    decimal? P75,
    decimal? P90,
    decimal Max,
    decimal? SpreadMultiple,
    decimal? P90ToP10);

/// <summary>Where a provider matched by the NPI/EIN/name filter sits within its group.</summary>
/// <param name="Rate">The provider's value from its matched rates (an NPI's own rate).</param>
/// <param name="RateLow">Lowest of those matched rates (equal to <paramref name="Rate"/> for a single rate).</param>
/// <param name="RateHigh">Highest of those matched rates.</param>
public sealed record ProviderPosition(
    string ProviderKey,
    string DisplayName,
    decimal Rate,
    int? Percentile,
    decimal? VsMedian,
    decimal? AboveP25,
    decimal RateLow,
    decimal RateHigh);

public sealed record BilledAs(string BillingCodeType, string BillingCode, string Label);

public sealed record PlaceRef(string Pos, string Name);

public sealed record ModifierRef(string Code, string Label);

public sealed record RateRowsPage(int Total, int Page, int PageSize, IReadOnlyList<RateRow> Items);

/// <summary>
/// One provider (tax ID) in one comparison group. With an NPI, EIN or name filter, the row describes only the rates that
/// matched (an NPI's own rate, not its tax ID's blend); the group's statistics stay market-wide.
/// </summary>
/// <param name="Ein">Full EIN, or null when masked (IRS never issues the prefix; it may be a Social Security number).</param>
/// <param name="EinLevel">At least one rate behind the row comes from a contract with the whole tax ID (the file lists
/// npi [0] for it). <paramref name="NpiCount"/> counts only the NPIs the file lists.</param>
/// <param name="BusinessName">
/// The name on the provider group(s) behind <c>FirstRateId</c>, the facility paid <c>Rate</c> (among the groups that
/// matched when a provider filter is on). <c>OtherNames</c> lists the tax ID's other names.
/// </param>
/// <param name="Rate">The provider's rate. With several rates, their NPI-weighted median: always one it is really paid.</param>
/// <param name="Band">"low" (at or below the 25th percentile and under the median), "high" (at or above the 75th and over
/// the median), or "typical". Null for small groups.</param>
/// <param name="NpiCount">Distinct NPIs the provider lists under the row's rates.</param>
/// <param name="FirstRateId">The rate shown in the source drawer: the one carrying <paramref name="Rate"/>.</param>
/// <param name="RateIds">Every rate behind the row (for the NPI list, which accepts up to 300 more besides its own).</param>
public sealed record RateRow(
    string GroupKey,
    string ProviderKey,
    string? Ein,
    string EinDisplay,
    bool EinMasked,
    bool EinLevel,
    string BusinessName,
    IReadOnlyList<string> OtherNames,
    decimal Rate,
    decimal RateLow,
    decimal RateHigh,
    bool MultipleRates,
    decimal? VsMedian,
    int? Percentile,
    string? Band,
    int NpiCount,
    int Occurrences,
    int RateCount,
    long FirstRateId,
    bool Matched,
    IReadOnlyList<long> RateIds);

/// <param name="Kind">Stable identifier the UI can switch on, e.g. "code_not_found", "npi_check_digit".</param>
/// <param name="Severity">"info" or "warning".</param>
public sealed record SearchHint(string Kind, string Severity, string Message, IReadOnlyList<HintLink> Links);

/// <summary>A suggested follow-up search, as query parameters to apply to the current search; a null value removes that parameter.</summary>
public sealed record HintLink(string Label, IReadOnlyDictionary<string, string?> Query);

/// <summary>How much clean-up sits behind these results.</summary>
public sealed record SearchQuality(
    long PublishedRows,
    int UniqueRates,
    long DuplicatesCollapsed,
    int ProvidersWithMultipleRates,
    int MaskedTaxIds,
    int WholeTaxIdContracts);
