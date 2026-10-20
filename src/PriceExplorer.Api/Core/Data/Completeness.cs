using System.Data.Common;
using Dapper;
using DuckDB.NET.Data;
using PriceExplorer.Api.Core.Domain;

namespace PriceExplorer.Api.Core.Data;

/// <summary>Every saved snapshot × product's completeness (see <see cref="SnapshotCompleteness"/>), from ProductCounts.sql.</summary>
internal static class Completeness
{
    public static async Task<IReadOnlyDictionary<(int SnapshotId, string ProductKey), ProductCompleteness>> ReadAsync(
        DuckDBConnection connection, DbTransaction? transaction = null) =>
        SnapshotCompleteness.Score(await connection.QueryAsync<ProductCounts>(
            SqlFile.For<DuckDbConnectionFactory>("ProductCounts.sql"), transaction: transaction));
}
