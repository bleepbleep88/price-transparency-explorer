using Dapper;
using DuckDB.NET.Data;
using PriceExplorer.Api.Core.Exceptions;

namespace PriceExplorer.Api.Core.Data;

public sealed record ResolvedSnapshot(int SnapshotId, DateOnly? IndexDate, bool IsLatest);

/// <summary>Picks the snapshot a query runs against: the one asked for, or the newest one that loaded.</summary>
internal static class SnapshotResolver
{
    public static async Task<ResolvedSnapshot> ResolveAsync(DuckDBConnection connection, int? snapshotId)
    {
        var snapshot = await connection.QuerySingleOrDefaultAsync<ResolvedSnapshot>(
            SqlFile.For<DuckDbConnectionFactory>("ResolveSnapshot.sql"), new { snapshotId });

        return snapshot ?? throw (snapshotId is null
            ? new IndexNotReadyException("No snapshot has finished loading yet.")
            : new NotFoundException($"Snapshot {snapshotId} doesn't exist or didn't load.", "snapshot_not_found"));
    }
}
