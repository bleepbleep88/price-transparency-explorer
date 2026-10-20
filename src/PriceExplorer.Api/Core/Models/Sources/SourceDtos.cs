namespace PriceExplorer.Api.Core.Models.Sources;

/// <summary>One snapshot (index file) and every data file it lists, with what loading did to each.</summary>
/// <param name="Status">loading | ready | partial | failed, from its rate files only.</param>
public sealed record SourceSnapshot(
    int SnapshotId,
    DateOnly? IndexDate,
    string IndexUrl,
    DateOnly? IndexLastUpdatedOn,
    string? IndexSha256,
    string? SchemaVersion,
    string? ReportingEntityName,
    string? ReportingEntityType,
    string Status,
    string? Error,
    bool IsLatest,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<SourceFile> Files);

/// <summary>A data file the index lists: where it came from, its three dates, and how it was loaded and cleaned.</summary>
/// <param name="Kind">"in_network" (negotiated rates) or "allowed_amount" (out-of-network allowed amounts).</param>
/// <param name="Status">pending | loaded | partial | failed. A failed refresh keeps "loaded" and says so in the warning.</param>
/// <param name="Counts">Null until the file has been loaded once.</param>
public sealed record SourceFile(
    int FileId,
    string Kind,
    string ProductKey,
    string ProductLabel,
    string Url,
    string? Description,
    string Status,
    string? Error,
    string? Warning,
    SourceFileDates Dates,
    long? ContentLength,
    string? Sha256,
    string? ETag,
    string? FileSchemaVersion,
    string? ReportingEntityName,
    SourceFileCounts? Counts,
    IReadOnlyList<SourceReject> Rejects,
    SourceFileTiming Timing);

/// <summary>The three dates a file has. They differ: 2026-08-31 in the index's name, 2026-09-02 inside, served 2026-09-04.</summary>
/// <param name="IndexDate">The date in the index file's name (the snapshot).</param>
/// <param name="FileLastUpdatedOn">last_updated_on inside the file.</param>
/// <param name="HttpLastModified">The payer's server's Last-Modified header.</param>
public sealed record SourceFileDates(DateOnly? IndexDate, DateOnly? FileLastUpdatedOn, DateTimeOffset? HttpLastModified);

/// <summary>What the file held and what loading did. Fields that don't apply to a kind of file are null.</summary>
/// <param name="Published">Prices (rate files) or payments (allowed-amount files) as published, before anything was dropped.</param>
/// <param name="Unique">What's left once exact duplicates collapse: unique rates or unique payments.</param>
/// <param name="Rejected">Published prices or payments dropped by a reject (see the file's rejects).</param>
/// <param name="ProviderGroups">Provider groups (rate files) or tax ID entries (allowed-amount files).</param>
/// <param name="InvalidNpis">Distinct NPIs that fail the check digit. Kept and flagged.</param>
/// <param name="UnverifiedEins">Distinct EINs with a prefix the IRS never issues. Results mask them.</param>
/// <param name="WholeTaxIdGroups">Rate files: provider groups listing npi [0], a contract with the whole tax ID.</param>
/// <param name="DanglingReferences">Rate files: provider references that rates use but the file never defines.</param>
/// <param name="CodesWithZerosRestored">Distinct codes published without their leading zeros (490 for 0490), restored.</param>
/// <param name="RatesFromNotes">Rate files: unique facility rates whose procedure code came from additional_information.</param>
/// <param name="EinsWithZeroRestored">Allowed-amount files: distinct EINs published as 8 digits, given back their leading zero.</param>
public sealed record SourceFileCounts(
    int? Items,
    long? Published,
    int? Unique,
    long? DuplicatesRemoved,
    long? Rejected,
    int? ProviderGroups,
    int? DistinctNpis,
    int? DistinctEins,
    int? InvalidNpis,
    int UnverifiedEins,
    int? WholeTaxIdGroups,
    int? DanglingReferences,
    int CodesWithZerosRestored,
    int? RatesFromNotes,
    int? EinsWithZeroRestored);

/// <summary>One kind of problem found while reading a file.</summary>
/// <param name="Reason">Stable machine-readable reason, e.g. "rate_not_a_number".</param>
/// <param name="Description">What it is and what loading did about it, in plain words.</param>
/// <param name="Example">The first one seen: a JSON path or a value. Tax ID values are masked to their last 4 digits.</param>
public sealed record SourceReject(string Reason, string Description, long Count, string? Example);

/// <param name="LoadedAt">When the data shown was saved. An unchanged file re-checked later keeps its first load's times.</param>
public sealed record SourceFileTiming(int? DownloadMs, int? ParseMs, DateTimeOffset? LoadedAt);
