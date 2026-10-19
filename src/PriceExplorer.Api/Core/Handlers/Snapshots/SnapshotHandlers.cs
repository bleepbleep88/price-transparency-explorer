using Dapper;
using MediatR;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Exceptions;
using PriceExplorer.Api.Core.Models.Snapshots;
using PriceExplorer.Api.Core.Queries.Snapshots;

namespace PriceExplorer.Api.Core.Handlers.Snapshots;

public sealed class ListSnapshotsHandler(DuckDbConnectionFactory connections) : IRequestHandler<ListSnapshotsQuery, IReadOnlyList<SnapshotSummary>>
{
    public async Task<IReadOnlyList<SnapshotSummary>> Handle(ListSnapshotsQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<SnapshotFileRow>(SqlFile.For<ListSnapshotsHandler>("ListSnapshots.sql"));

        var snapshots = rows
            .GroupBy(row => row.SnapshotId)
            .Select(group => (Rows: group.ToList(), First: group.First()))
            .ToList();

        var latestId = await connection.ExecuteScalarAsync<int?>(SqlFile.For<DuckDbConnectionFactory>("LatestSnapshot.sql"));

        return snapshots.Select(s => new SnapshotSummary(
            s.First.SnapshotId,
            s.First.IndexDate,
            s.First.IndexUrl,
            s.First.IndexLastUpdatedOn,
            s.First.ReportingEntityName,
            s.First.ReportingEntityType,
            s.First.SnapshotStatus,
            IsLatest: s.First.SnapshotId == latestId,
            Products: s.Rows
                .Where(row => row.FileId is not null)
                .Select(row => new ProductFileSummary(
                    row.FileId!.Value, row.ProductKey!, row.ProductLabel!, row.Url!, row.FileStatus!, row.Error, row.Warning,
                    row.FileLastUpdatedOn, UtcTimestamps.From(row.HttpLastModified), row.ContentLength, row.Sha256,
                    (int)row.PlanEntries, (int)row.DistinctPlanIds, row.Items, row.PricesPublished, row.UniqueRates,
                    row.DuplicatesRemoved, row.DistinctNpis, row.DistinctEins, UtcTimestamps.From(row.LoadedAt)))
                .ToList()))
            .ToList();
    }

    private sealed record SnapshotFileRow(
        int SnapshotId,
        DateOnly? IndexDate,
        string IndexUrl,
        DateOnly? IndexLastUpdatedOn,
        string? ReportingEntityName,
        string? ReportingEntityType,
        string SnapshotStatus,
        int? FileId,
        string? ProductKey,
        string? ProductLabel,
        string? Url,
        string? FileStatus,
        string? Error,
        string? Warning,
        DateOnly? FileLastUpdatedOn,
        DateTime? HttpLastModified,
        long? ContentLength,
        string? Sha256,
        long PlanEntries,
        long DistinctPlanIds,
        int? Items,
        long? PricesPublished,
        int? UniqueRates,
        long? DuplicatesRemoved,
        int? DistinctNpis,
        int? DistinctEins,
        DateTime? LoadedAt);
}

public sealed class ListPlansHandler(DuckDbConnectionFactory connections) : IRequestHandler<ListPlansQuery, SnapshotPlans>
{
    public async Task<SnapshotPlans> Handle(ListPlansQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var exists = await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM snapshots WHERE snapshot_id = $snapshotId", new { snapshotId = request.SnapshotId }) > 0;
        if (!exists)
        {
            throw new NotFoundException($"Snapshot {request.SnapshotId} doesn't exist.", "snapshot_not_found");
        }

        var rows = await connection.QueryAsync<PlanRow>(
            SqlFile.For<ListPlansHandler>("ListPlans.sql"), new { snapshotId = request.SnapshotId });

        var products = rows
            .GroupBy(row => (row.ProductKey, row.ProductLabel))
            .Select(product => new ProductPlans(
                product.Key.ProductKey,
                product.Key.ProductLabel,
                PlanEntries: product.Count(),
                Plans: product
                    .GroupBy(row => (row.PlanId, row.PlanIdType, row.MarketType, row.IssuerName, row.SponsorName))
                    .Select(plan => new PlanSummary(
                        plan.Key.PlanId, plan.Key.PlanIdType, plan.Key.MarketType, plan.Key.IssuerName, plan.Key.SponsorName,
                        plan.Select(row => row.PlanName).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList()))
                    .ToList()))
            .ToList();

        return new SnapshotPlans(request.SnapshotId, products);
    }

    private sealed record PlanRow(
        string ProductKey,
        string ProductLabel,
        string? PlanId,
        string? PlanIdType,
        string? MarketType,
        string? IssuerName,
        string? SponsorName,
        string? PlanName);
}
