using PriceExplorer.Api.Core.Domain;

namespace PriceExplorer.Api.Core.Models.Snapshots;

/// <summary>One loaded index (snapshot) and the rate files it points to.</summary>
public sealed record SnapshotSummary(
    int SnapshotId,
    DateOnly? IndexDate,
    string IndexUrl,
    DateOnly? IndexLastUpdatedOn,
    string? ReportingEntityName,
    string? ReportingEntityType,
    string Status,
    bool IsLatest,
    IReadOnlyList<ProductFileSummary> Products);

/// <summary>A product's in-network rate file, with where it came from and what loading found.</summary>
/// <param name="Completeness">How complete the product's file looks next to nearby snapshots; null until it's saved.</param>
public sealed record ProductFileSummary(
    int FileId,
    string ProductKey,
    string ProductLabel,
    string Url,
    string Status,
    string? Error,
    string? Warning,
    DateOnly? FileLastUpdatedOn,
    DateTimeOffset? HttpLastModified,
    long? ContentLength,
    string? Sha256,
    int PlanEntries,
    int DistinctPlanIds,
    int? Items,
    long? PricesPublished,
    int? UniqueRates,
    long? DuplicatesRemoved,
    int? DistinctNpis,
    int? DistinctEins,
    DateTimeOffset? LoadedAt,
    ProductCompleteness? Completeness);

public sealed record SnapshotPlans(int SnapshotId, IReadOnlyList<ProductPlans> Products);

public sealed record ProductPlans(string ProductKey, string ProductLabel, int PlanEntries, IReadOnlyList<PlanSummary> Plans);

/// <summary>A plan ID with every name it's published under (cost-sharing variants share one ID).</summary>
public sealed record PlanSummary(
    string? PlanId,
    string? PlanIdType,
    string? MarketType,
    string? IssuerName,
    string? SponsorName,
    IReadOnlyList<string> Names);
