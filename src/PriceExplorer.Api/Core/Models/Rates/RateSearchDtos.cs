using System.Text.Json.Serialization;
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
/// <param name="Specialty">The specialty filter as typed.</param>
/// <param name="County">The location filter as typed.</param>
/// <param name="CountyPlace">The place the location filter matched, the way to show it ("Manhattan", "Delaware County",
/// "New York City"); null without a location filter or when it matches no one.</param>
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
    string? Specialty,
    string? County,
    string? CountyPlace,
    string? Group,
    string Sort,
    int Page,
    int PageSize);

public sealed record SnapshotRef(int SnapshotId, DateOnly? IndexDate, bool IsLatest);

/// <param name="Name">A short name: the plan's own, else CMS's HCPCS name (cut at a word near 100 characters).</param>
/// <param name="Description">The plan's description, or CMS's full HCPCS name when the short one was cut.</param>
/// <param name="DescriptionSource">"payer_file" when the plan's file describes the code, "cms_hcpcs" when the name comes
/// from CMS's HCPCS Level II list instead, otherwise "none".</param>
/// <param name="Category">How the code is priced: its kind of service and the unit a rate pays for.</param>
/// <param name="Classification">What the code is: CMS's RBCS category, subcategory and family, or the code-range category.</param>
/// <param name="DoseUnit">For drugs: the amount one HCPCS unit holds, read from the code's name ("10 mg" for J2704). Null
/// when the name doesn't state one.</param>
/// <param name="FoundVia">How the code appears in the files, e.g. "Billing code" or "Revenue code 0490 (Ambulatory surgery)".</param>
public sealed record CodeInfo(
    string CodeType,
    string Code,
    string? Name,
    string? Description,
    string DescriptionSource,
    ServiceCategory Category,
    CodeClassification Classification,
    DoseUnit? DoseUnit,
    string? LevelFamily,
    IReadOnlyList<string> FoundVia);

/// <summary>What a code is, for browsing and filtering (GET /api/v1/categories, the category filter on /codes).</summary>
/// <param name="Key">The most specific category key: an RBCS family ("PG004") or subcategory ("PG"), or a code-range
/// category ("facility").</param>
/// <param name="Category">"Procedures", "Visits and care management", or a code-range category ("Facility rate (revenue code only)").</param>
/// <param name="Subcategory">CMS's RBCS subcategory, e.g. "Digestive/Gastrointestinal"; null for code-range categories.</param>
/// <param name="Family">CMS's RBCS family, e.g. "Lower GI Endoscopy - Other"; null when CMS gives none.</param>
/// <param name="Source">"cms_rbcs" (CMS's Restructured BETOS Classification System) or "code_ranges" (CPT sections and
/// HCPCS letter groups).</param>
public sealed record CodeClassification(string Key, string Category, string? Subcategory, string? Family, string Source);

/// <summary>How much of a drug one HCPCS unit is, as the code's name states it.</summary>
/// <param name="Amount">10 for "10 mg"; 1 for "per ml".</param>
/// <param name="Unit">"mg", "mcg", "g", "ml", "unit" or "meq".</param>
/// <param name="Text">As written in the name: "10 mg", "per ml", "250 cc", "1 unit".</param>
public sealed record DoseUnit(decimal Amount, string Unit, string Text);

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
/// matched when a provider filter is on): the practice's own name when the plan's is a network's shared name or comes
/// from an unreliable file (<see cref="ListedAs"/> then gives the plan's). <c>OtherNames</c> lists the tax ID's other names.
/// </param>
/// <param name="Rate">The provider's rate. With several rates, their NPI-weighted median: always one it is really paid.</param>
/// <param name="Band">"low" (at or below the 25th percentile and under the median), "high" (at or above the 75th and over
/// the median), or "typical". Null for small groups.</param>
/// <param name="NpiCount">Distinct NPIs the provider lists under the row's rates.</param>
/// <param name="FirstRateId">The rate shown in the source drawer: the one carrying <paramref name="Rate"/>.</param>
/// <param name="WholeTaxIdRateId">
/// When <paramref name="EinLevel"/> comes from a whole-tax-ID contract other than <paramref name="FirstRateId"/>, that rate, so the
/// drawer can show the contract the flag describes. Null otherwise.
/// </param>
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
    IReadOnlyList<long> RateIds,
    long? WholeTaxIdRateId)
{
    /// <summary>Who the NPIs behind the row are, from the national provider registry. Null when no registry is loaded or the row lists no NPIs.</summary>
    public ProviderProfile? Profile { get; init; }

    /// <summary>"Check this" notes: the rate may be listed for a provider who doesn't perform the service.</summary>
    public IReadOnlyList<RowFlag> Flags { get; init; } = [];

    /// <summary>
    /// The plan's name as published when <see cref="BusinessName"/> replaced it ("Chinese American IPA Assoc/CAIPA"), several
    /// joined by "; ". Null when the plan's own name is shown.
    /// </summary>
    public string? ListedAs { get; init; }
}

/// <summary>Who the NPIs behind a row are, from the national provider registry (NPPES) via the bundled directory.</summary>
/// <param name="Npis">Distinct NPIs listed for the row's rates (the row's NPI count).</param>
/// <param name="NpisInDirectory">How many of them the registry has, deactivated ones included.</param>
/// <param name="Individuals">Active NPIs registered to individual clinicians; the others are organizations or deactivated.</param>
/// <param name="Deactivated">NPIs deactivated in the registry.</param>
/// <param name="Specialties">Each specialty with its NPI count: those matching the specialty filter first, then the most NPIs.</param>
/// <param name="Locations">Up to 3 practice places ("Manhattan, NY", "Rome, NY (Oneida County)"): those in the filtered
/// location first, then the most NPIs.</param>
/// <param name="LocationCount">How many places there are in all.</param>
public sealed record ProviderProfile(
    int Npis,
    int NpisInDirectory,
    int Individuals,
    int Deactivated,
    IReadOnlyList<SpecialtyCount> Specialties,
    IReadOnlyList<string> Locations,
    int LocationCount)
{
    /// <summary>Active radiologists, nuclear medicine physicians and radiology centers among the NPIs (for the imaging flag).</summary>
    [JsonIgnore]
    public int ImagingReaders { get; init; }

    /// <summary>Active cardiologists among the NPIs: they read cardiac CT, MRI and PET too.</summary>
    [JsonIgnore]
    public int CardiacReaders { get; init; }
}

/// <param name="Group">The NUCC grouping, e.g. "Hospitals" or "Ambulatory Health Care Facilities".</param>
public sealed record SpecialtyCount(string Specialty, string? Group, int Npis);

/// <summary>A "check this" note on a row.</summary>
/// <param name="Kind">Stable identifier, e.g. "imaging_without_radiologist".</param>
public sealed record RowFlag(string Kind, string Message);

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

/// <summary>
/// A visit code's level ladder: each level of its family side by side, for each plan, fee type, setting, modifier set and
/// rate type (each line is one search card's numbers per level). Lines where most levels have 5 or more providers come first.
/// </summary>
/// <param name="Family">"Office visit, established patient", "Office visit, new patient" or "Emergency room visit".</param>
/// <param name="Codes">The family's codes, lowest level first.</param>
public sealed record LevelLadder(SnapshotRef Snapshot, string Family, IReadOnlyList<LevelCode> Codes, IReadOnlyList<LevelLine> Lines);

/// <param name="Level">1 to 5 (99202 is level 2: level 1 of new-patient visits was deleted in 2021).</param>
public sealed record LevelCode(string Code, int Level);

/// <summary>One plan, fee type, setting, modifier set and rate type, across the levels it has rates for.</summary>
/// <param name="Key">The search's group key without the code.</param>
/// <param name="Unit">What one rate pays for, as the search card says ("per visit", "per visit (all-inclusive)").</param>
/// <param name="Levels">The levels with rates here, lowest first; a level with none is left out.</param>
public sealed record LevelLine(
    string Key,
    string ProductKey,
    string ProductLabel,
    string FeeType,
    string SettingKey,
    string SettingLabel,
    IReadOnlyList<ModifierRef> Modifiers,
    string NegotiatedType,
    string Unit,
    IReadOnlyList<LevelStep> Levels);

/// <summary>One level on one line: that level's search card, as numbers.</summary>
/// <param name="GroupKey">The search's group key for this level (<see cref="RateGroup.Key"/>).</param>
/// <param name="StatsLevel">"list" (under 5 providers: no statistics), "basic" (5–9: median and range), "full" (10+).</param>
public sealed record LevelStep(string Code, int Level, string GroupKey, int ProviderCount, string StatsLevel, RateStats Stats);
