using Dapper;
using MediatR;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Ingestion;
using PriceExplorer.Api.Core.Models.Sources;
using PriceExplorer.Api.Core.Queries.Sources;

namespace PriceExplorer.Api.Core.Handlers.Sources;

public sealed class ListSourcesHandler(DuckDbConnectionFactory connections) : IRequestHandler<ListSourcesQuery, IReadOnlyList<SourceSnapshot>>
{
    public async Task<IReadOnlyList<SourceSnapshot>> Handle(ListSourcesQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = (await connection.QueryAsync<SourceRow>(SqlFile.For<ListSourcesHandler>("ListSources.sql"))).ToList();
        var rejects = (await connection.QueryAsync<RejectRow>(SqlFile.For<ListSourcesHandler>("SourceRejects.sql")))
            .ToLookup(reject => reject.FileId);
        var latestId = await connection.ExecuteScalarAsync<int?>(SqlFile.For<DuckDbConnectionFactory>("LatestSnapshot.sql"));

        return rows
            .GroupBy(row => row.SnapshotId)
            .Select(snapshot =>
            {
                var first = snapshot.First();
                return new SourceSnapshot(
                    first.SnapshotId,
                    first.IndexDate,
                    first.IndexUrl,
                    first.IndexLastUpdatedOn,
                    first.IndexSha256,
                    first.SchemaVersion,
                    first.ReportingEntityName,
                    first.ReportingEntityType,
                    first.SnapshotStatus,
                    first.SnapshotError,
                    IsLatest: first.SnapshotId == latestId,
                    UtcTimestamps.From(first.UpdatedAt),
                    snapshot.Where(row => row.FileId is not null).Select(row => ToFile(row, rejects[row.FileId!.Value])).ToList());
            })
            .ToList();
    }

    private static SourceFile ToFile(SourceRow row, IEnumerable<RejectRow> rejects)
    {
        var rateFile = row.Kind == SourceKinds.InNetwork;
        return new SourceFile(
            row.FileId!.Value,
            row.Kind!,
            row.ProductKey!,
            row.ProductLabel!,
            row.Url!,
            row.Description,
            row.FileStatus!,
            row.Error,
            row.Warning,
            new SourceFileDates(row.IndexDate, row.FileLastUpdatedOn, UtcTimestamps.From(row.HttpLastModified)),
            row.ContentLength,
            row.Sha256,
            row.ETag,
            row.FileSchemaVersion,
            row.FileReportingEntityName,
            row.LoadedAt is null
                ? null
                : new SourceFileCounts(
                    row.Items,
                    row.Published,
                    row.UniqueRows,
                    row.DuplicatesRemoved,
                    row.Rejected,
                    row.ProviderGroups,
                    row.DistinctNpis,
                    row.DistinctEins,
                    row.InvalidNpis,
                    (int)row.UnverifiedEins,
                    rateFile ? row.WholeTaxIdGroups : null,
                    rateFile ? row.DanglingReferences : null,
                    (int)row.CodesWithZerosRestored,
                    rateFile ? (int)(row.RatesFromNotes ?? 0) : null,
                    rateFile ? null : (int)(row.EinsWithZeroRestored ?? 0)),
            rejects
                .Select(reject => new SourceReject(
                    reject.Reason, RejectReasonText.Describe(reject.Reason), reject.Count, RejectReasonText.Example(reject.Reason, reject.Example)))
                .ToList(),
            new SourceFileTiming(row.DownloadMs, row.ParseMs, UtcTimestamps.From(row.LoadedAt)));
    }

    private sealed record SourceRow(
        int SnapshotId,
        DateOnly? IndexDate,
        string IndexUrl,
        DateOnly? IndexLastUpdatedOn,
        string? IndexSha256,
        string? SchemaVersion,
        string? ReportingEntityName,
        string? ReportingEntityType,
        string SnapshotStatus,
        string? SnapshotError,
        DateTime? UpdatedAt,
        int? FileId,
        string? Kind,
        string? ProductKey,
        string? ProductLabel,
        string? Url,
        string? Description,
        string? FileStatus,
        string? Error,
        string? Warning,
        DateOnly? FileLastUpdatedOn,
        DateTime? HttpLastModified,
        long? ContentLength,
        string? Sha256,
        string? ETag,
        string? FileSchemaVersion,
        string? FileReportingEntityName,
        int? Items,
        long? Published,
        int? UniqueRows,
        long? DuplicatesRemoved,
        long? Rejected,
        int? ProviderGroups,
        int? DistinctNpis,
        int? DistinctEins,
        int? InvalidNpis,
        int? WholeTaxIdGroups,
        int? DanglingReferences,
        long UnverifiedEins,
        long CodesWithZerosRestored,
        long? RatesFromNotes,
        long? EinsWithZeroRestored,
        int? DownloadMs,
        int? ParseMs,
        DateTime? LoadedAt);

    private sealed record RejectRow(int FileId, string Reason, long Count, string? Example);
}
