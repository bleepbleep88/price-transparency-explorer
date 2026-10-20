using PriceExplorer.Api.Core.Models.Rates;

namespace PriceExplorer.Api.Core.Models.Statistics;

/// <summary>A snapshot's saved comparison groups (group_stats), each with its providers' values (provider_values).</summary>
/// <param name="BuiltAt">When the snapshot's statistics were built; null until its first build finishes.</param>
/// <param name="Current">
/// The saved numbers are what search shows now. False while they're out of date: a rate file was saved again or the
/// index renamed a product since the build (reloads rebuild them once every file is loaded), the app's code changed, a
/// rate in them has expired, or no build has finished. Don't present them as current then: say they're being updated,
/// or use search.
/// </param>
/// <param name="ExpiresOn">The earliest expiration date among the rates the statistics include; null if none ends.</param>
internal sealed record GroupStatsResult(
    SnapshotRef Snapshot, DateTimeOffset? BuiltAt, bool Current, DateOnly? ExpiresOn, IReadOnlyList<GroupStats> Groups);

/// <summary>
/// One comparison group's statistics over one value per provider: the numbers its search card shows
/// (<see cref="RateGroup"/>), plus every provider's value in it.
/// </summary>
/// <param name="Key">The search's group key (<see cref="RateGroup.Key"/>): the parts below joined with '|'.</param>
/// <param name="Modifiers">Sorted, comma-separated modifier codes; "" = none.</param>
/// <param name="Unit">What one rate pays for ("per procedure", "per facility charge", "per HCPCS unit …"), as the card says.</param>
/// <param name="PerUnit">The rates are prices per unit (a drug or supply), not per visit or procedure.</param>
/// <param name="Providers">Every provider in the group, by provider key (only the one asked for with a provider filter).</param>
internal sealed record GroupStats(
    string Key,
    string ProductKey,
    string ProductLabel,
    string CodeType,
    string Code,
    string FeeType,
    string SettingKey,
    string Modifiers,
    string NegotiatedType,
    string Unit,
    bool PerUnit,
    int ProviderCount,
    int RateCount,
    string StatsLevel,
    RateStats Stats,
    decimal? LowerQuartileGap,
    IReadOnlyList<ProviderValue> Providers);

/// <summary>One provider (tax ID) in one comparison group, as an unfiltered search row shows it (<see cref="RateRow"/>).</summary>
/// <param name="Ein">Full EIN, or null when masked (a prefix the IRS never issues) or when the tax ID isn't an EIN.</param>
/// <param name="EinLevel">At least one of its rates comes from a contract with the whole tax ID (npi [0]).</param>
/// <param name="BusinessName">The name on the provider group(s) behind <paramref name="FirstRateId"/>.</param>
/// <param name="Value">The NPI-weighted (lower) median of its rates in the group: always a rate it is really paid.</param>
/// <param name="NpiWeight">The total weight behind <paramref name="Value"/>: each rate's NPI count (at least 1), summed.</param>
/// <param name="FirstRateId">The rate carrying <paramref name="Value"/> that lists the most NPIs (the row's source). Rate
/// ids are renumbered when a file is saved again, so it points at the rate it describes only while the result is
/// <see cref="GroupStatsResult.Current"/>.</param>
/// <param name="WholeTaxIdRateId">When <paramref name="EinLevel"/> comes from a whole-tax-ID contract other than
/// <paramref name="FirstRateId"/>, that rate; null otherwise. Like <paramref name="FirstRateId"/>, only while current.</param>
internal sealed record ProviderValue(
    string ProviderKey,
    string? Ein,
    string EinDisplay,
    bool EinMasked,
    bool EinLevel,
    string BusinessName,
    decimal Value,
    decimal Low,
    decimal High,
    int RateCount,
    long NpiWeight,
    long FirstRateId,
    long? WholeTaxIdRateId);
