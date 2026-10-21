namespace PriceExplorer.Api.Core.Models.Rates;

/// <summary>Everything needed to trace one rate back to the exact spot in the payer's file.</summary>
/// <param name="JsonPath">Where the first published copy sits in the in-network file.</param>
/// <param name="BillingCodePublished">The code exactly as the file wrote it (e.g. "490" before restoring the leading zero).</param>
/// <param name="CodeSource">"billing_code", or "additional_information" when the procedure code came from the notes field.</param>
/// <param name="Occurrences">How many identical published rows collapsed into this rate.</param>
/// <param name="ItemLabelCount">How many different item names those rows were published under (revenue-code items repeat blocks).</param>
public sealed record RateSource(
    long RateId,
    RateSourceFile File,
    RateSourceSnapshot Snapshot,
    string JsonPath,
    string BillingCodeType,
    string BillingCode,
    string BillingCodePublished,
    string CodeType,
    string Code,
    string CodeSource,
    string? AdditionalInformation,
    string NegotiationArrangement,
    string NegotiatedType,
    decimal Rate,
    DateOnly? ExpirationDate,
    string BillingClass,
    string Setting,
    IReadOnlyList<PlaceRef> Places,
    IReadOnlyList<ModifierRef> Modifiers,
    int Occurrences,
    int ItemLabelCount,
    IReadOnlyList<string> ItemLabelSamples,
    IReadOnlyList<RateSourceProviderGroup> ProviderGroups,
    int ProviderGroupCount);

public sealed record RateSourceFile(
    int FileId,
    string ProductKey,
    string ProductLabel,
    string Url,
    string? Sha256,
    DateOnly? LastUpdatedOn,
    DateTimeOffset? HttpLastModified,
    long? ContentLength,
    string? ReportingEntityName,
    string? SchemaVersion);

public sealed record RateSourceSnapshot(int SnapshotId, string IndexUrl, DateOnly? IndexDate, DateOnly? IndexLastUpdatedOn);

/// <param name="ProviderGroupId">provider_group_id in the file (negative for groups written inline on the rate).</param>
/// <param name="WholeTaxId">The file lists npi [0]: the contract covers anyone billing under this tax ID.</param>
/// <param name="BusinessName">The name on the group, as published.</param>
/// <param name="ProviderMatch">This group belongs to the provider passed to the source endpoint (listed first).</param>
public sealed record RateSourceProviderGroup(
    int ProviderGroupId,
    int GroupIndex,
    string TaxIdDisplay,
    bool TaxIdMasked,
    string? BusinessName,
    int NpiCount,
    bool WholeTaxId,
    string? NetworkNames,
    bool ProviderMatch)
{
    /// <summary>
    /// The practice's own name that lists show instead of <see cref="BusinessName"/> (a network's shared name, or a name
    /// from an unreliable file). Null when lists show the published name.
    /// </summary>
    public string? ShownName { get; init; }
}

public sealed record RateNpisPage(long RateId, string? ProviderKey, int Total, int Page, int PageSize, IReadOnlyList<ListedNpi> Items);

/// <param name="Valid">Passes the NPI check digit. Invalid NPIs are shown as published.</param>
/// <param name="Rate">The rate this NPI is listed for. When several of the requested rates list it, the lowest.</param>
/// <param name="RateHigh">The highest of the requested rates that list this NPI (equal to <paramref name="Rate"/> when only one does).</param>
public sealed record ListedNpi(long Npi, bool Valid, decimal Rate, decimal RateHigh);

public sealed record CodeSuggestion(
    string CodeType,
    string Code,
    string? Name,
    string? Description,
    string Category,
    string Unit,
    bool BilledDirectly,
    IReadOnlyList<string> FacilityRevenueCodes,
    long Rates);

public sealed record ProviderSuggestion(
    string ProviderKey,
    string TaxIdDisplay,
    bool TaxIdMasked,
    string BusinessName,
    IReadOnlyList<string> OtherNames,
    long Rates);

/// <param name="Locations">New York counties and NYC boroughs for the location filter; empty without a provider directory.</param>
/// <param name="Directory">Where the provider directory came from; null when none is loaded.</param>
public sealed record ReferenceLists(
    IReadOnlyList<SettingOption> Settings, IReadOnlyList<string> Sorts, IReadOnlyList<LocationOption> Locations, DirectorySource? Directory);

public sealed record SettingOption(string Key, string Label, string? Note);

/// <summary>Where the loaded provider directory came from (reference/provider_directory.source.json).</summary>
/// <param name="Release">The NPPES monthly release it was built from, e.g. "October 2026"; null when unknown.</param>
/// <param name="BuiltAt">When the builder tool made the bundled file.</param>
/// <param name="NpisLoaded">NPIs in the loaded directory.</param>
/// <param name="Source">The NPPES download it was built from.</param>
public sealed record DirectorySource(string? Release, DateTimeOffset? BuiltAt, long NpisLoaded, string? Source);

/// <summary>A New York county or NYC borough where NPIs in the files practice.</summary>
/// <param name="Label">What the location filter accepts: "Manhattan", "Monroe".</param>
/// <param name="Npis">NPIs practicing there.</param>
/// <param name="County">The county's own name, which the filter accepts too: "Kings County" for Brooklyn, "Monroe County".</param>
public sealed record LocationOption(string Label, bool IsBorough, long Npis, string County);

/// <param name="Group">"Physician" for a physician specialty ("Gastroenterology"); null for other provider types.</param>
/// <param name="Npis">NPIs in the files with this specialty.</param>
public sealed record SpecialtySuggestion(string Specialty, string? Group, long Npis);
