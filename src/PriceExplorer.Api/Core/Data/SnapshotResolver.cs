using Dapper;
using DuckDB.NET.Data;
using PriceExplorer.Api.Core.Exceptions;

namespace PriceExplorer.Api.Core.Data;

public sealed record ResolvedSnapshot(int SnapshotId, DateOnly? IndexDate, bool IsLatest);

/// <summary>
/// Picks the snapshot a query runs against: the one asked for, or the default one (LatestSnapshot.sql, the same rule
/// readiness uses). A snapshot qualifies once it has saved rate data, even if a run stopped before all its files.
/// </summary>
internal static class SnapshotResolver
{
    public static async Task<ResolvedSnapshot> ResolveAsync(DuckDBConnection connection, int? snapshotId)
    {
        var latestId = await connection.ExecuteScalarAsync<int?>(SqlFile.For<DuckDbConnectionFactory>("LatestSnapshot.sql"));
        var id = snapshotId ?? latestId ?? throw new IndexNotReadyException("No rate data has been saved yet.");

        var row = await connection.QuerySingleOrDefaultAsync<SnapshotRow>(
            SqlFile.For<DuckDbConnectionFactory>("ResolveSnapshot.sql"), new { snapshotId = id })
            ?? throw new NotFoundException($"Snapshot {id} doesn't exist or has no rate data saved.", "snapshot_not_found");

        return new ResolvedSnapshot(row.SnapshotId, row.IndexDate, row.SnapshotId == latestId);
    }

    private sealed record SnapshotRow(int SnapshotId, DateOnly? IndexDate);
}
