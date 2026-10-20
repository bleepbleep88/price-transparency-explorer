using System.Data.Common;
using Dapper;
using DuckDB.NET.Data;

namespace PriceExplorer.Api.Core.Data;

/// <summary>
/// Whether a snapshot's saved statistics (group_stats, provider_values) still match what search shows. One rule,
/// SavedStatistics.sql, serves both the statistics build, which rebuilds the snapshots that aren't current, and the
/// reader, which tells callers.
/// </summary>
internal static class SavedStatistics
{
    /// <summary>
    /// The statistics are the search's output, so they depend on this app's code (the SQL, the assembler and the reference
    /// tables, all in this assembly) as much as on the saved rates. Each build records the app build that made it, so a
    /// code change rebuilds them instead of leaving numbers search no longer shows. Deterministic builds keep this id
    /// until the code changes.
    /// </summary>
    public static readonly string CodeVersion = typeof(SavedStatistics).Assembly.ManifestModule.ModuleVersionId.ToString("N");

    /// <summary>Every snapshot search can serve, newest first, or only <paramref name="snapshotId"/>.</summary>
    public static async Task<IReadOnlyList<StatisticsState>> StateAsync(
        DuckDBConnection connection, int? snapshotId = null, DbTransaction? transaction = null) =>
        (await connection.QueryAsync<StatisticsState>(
            SqlFile.For<DuckDbConnectionFactory>("SavedStatistics.sql"), new { snapshotId, codeVersion = CodeVersion }, transaction)).ToList();
}

/// <summary>A snapshot's saved statistics, and whether they still match what search shows.</summary>
/// <param name="RateFiles">The snapshot's saved rate files as they are now: what a build records it was built from.</param>
/// <param name="BuiltAt">When its statistics were last built (UTC); null if never.</param>
/// <param name="ExpiresOn">The earliest expiration date among the rates the statistics include; null if none ends.</param>
/// <param name="Current">Built from the rate files as they are now, by this build of the app, and no rate in them has expired.</param>
internal sealed record StatisticsState(
    int SnapshotId, DateOnly? IndexDate, string RateFiles, DateTime? BuiltAt, DateOnly? ExpiresOn, bool Current)
{
    public string Label => IndexDate is { } date ? $"snapshot {date:yyyy-MM-dd}" : $"snapshot {SnapshotId}";
}
