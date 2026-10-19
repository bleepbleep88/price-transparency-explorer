using DuckDB.NET.Data;
using PriceExplorer.Api.Core.Ingestion;

namespace PriceExplorer.Api.Core.Data;

/// <summary>
/// Opens connections to the embedded DuckDB file. Connections in one process share the database, and readers see a
/// consistent snapshot while ingestion writes in a transaction. One anchor connection stays open for the app's
/// lifetime so each request's connection opens instantly.
/// </summary>
public sealed class DuckDbConnectionFactory : IDisposable
{
    private readonly string _connectionString;
    private readonly Lazy<DuckDBConnection> _anchor;

    public DuckDbConnectionFactory(DataPaths paths)
    {
        _connectionString = $"Data Source={paths.Database}";
        _anchor = new Lazy<DuckDBConnection>(() =>
        {
            var connection = new DuckDBConnection(_connectionString);
            connection.Open();
            return connection;
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<DuckDBConnection> OpenAsync(CancellationToken cancellationToken)
    {
        _ = _anchor.Value;
        var connection = new DuckDBConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public void Dispose()
    {
        if (_anchor.IsValueCreated)
        {
            _anchor.Value.Dispose();
        }
    }
}
