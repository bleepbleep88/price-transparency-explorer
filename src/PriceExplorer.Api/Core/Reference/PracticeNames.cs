using System.Diagnostics;
using Dapper;
using DuckDB.NET.Data;
using PriceExplorer.Api.Core.Data;

namespace PriceExplorer.Api.Core.Reference;

/// <summary>
/// Keeps practice_names current (PracticeNames.sql): the practice's own name for provider groups listed under a
/// network's shared name or in a file whose names are unreliable. It depends on the saved provider groups and the provider
/// directory, so it's rebuilt after the directory loads at startup and after each snapshot's rate files are saved.
/// Queries read it through practice_name() (PracticeNameMacro.sql).
/// </summary>
internal sealed class PracticeNames(DuckDbConnectionFactory connections, ILogger<PracticeNames> logger)
{
    /// <summary>Creates the macro, and an empty table when there is none, so queries never meet a missing one.</summary>
    public static Task EnsureAsync(DuckDBConnection connection, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(SqlFile.For<PracticeNames>("PracticeNameMacro.sql"), cancellationToken: cancellationToken));

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await RefreshAsync(connection, cancellationToken);
    }

    /// <summary>
    /// Rebuilds the table. A failure is logged and leaves the previous names in place: lists then show the plan's names
    /// for groups saved since, and search keeps working.
    /// </summary>
    public async Task RefreshAsync(DuckDBConnection connection, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(SqlFile.For<PracticeNames>("PracticeNames.sql"), cancellationToken: cancellationToken));
            var count = await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM practice_names");
            logger.LogInformation("Reference: {Count} provider groups show their practice's own name (built in {Ms} ms)", count, stopwatch.ElapsedMilliseconds);
        }
        catch (DuckDBException ex)
        {
            logger.LogWarning(ex, "Reference: couldn't rebuild practice names; lists keep the names they had");
        }
    }
}
