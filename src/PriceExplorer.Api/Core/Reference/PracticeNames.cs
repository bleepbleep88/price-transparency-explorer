using System.Diagnostics;
using Dapper;
using DuckDB.NET.Data;
using PriceExplorer.Api.Core.Data;

namespace PriceExplorer.Api.Core.Reference;

/// <summary>
/// Keeps name_checks (NameChecks.sql) and practice_names (PracticeNames.sql) current: which rate files' provider names
/// can be trusted, and the practice's own name for provider groups listed under a network's shared name or in a file
/// whose names can't. They depend on the saved provider groups and the provider directory, so they're rebuilt after the
/// directory loads at startup and after each snapshot's rate files are saved. Queries read them through practice_name()
/// (PracticeNameMacro.sql).
/// </summary>
internal sealed class PracticeNames(DuckDbConnectionFactory connections, ILogger<PracticeNames> logger)
{
    /// <summary>
    /// A name on this many tax IDs or more in a snapshot is a network's, not one practice's. In the Aug 31 files, 7,071 of
    /// 7,094 names belong to one tax ID and 15 to two to four (one practice's few entities, such as a PC and its surgery
    /// center); the 8 on five or more are networks (Catholic Medical Partners 123, Beacon IPA 107, CAIPA 24) and one faculty
    /// practice. On Sep 28, Ambetter's 10 are all networks ("Chinese American IPA Assoc/CAIPA": 253).
    /// </summary>
    internal const int SharedNameTaxIds = 5;

    /// <summary>
    /// A file that names fewer than this share of the tax IDs it has in common with another file the same way, against
    /// every file it's compared with, has unreliable names. Any two of the other files agree on 85 to 100% of the tax IDs
    /// they share; the Sep 28 Essential Plan file agrees with them on 9 to 11%. Half sits far from both.
    /// </summary>
    internal const double ReliableAgreement = 0.5;

    /// <summary>Two files are compared only over at least this many tax IDs in common, so their share means something.</summary>
    internal const int ComparableTaxIds = 100;

    /// <summary>
    /// A file is judged against at least this many others, so it's the odd one out: two files that disagree only with
    /// each other (one month, two plans) are both kept, since nothing says which is wrong.
    /// </summary>
    internal const int FilesToJudgeBy = 2;

    /// <summary>
    /// What a row from an unreliable file reads when no other file, nor the registry, names its tax ID: the file's own name
    /// is likely another provider's ("Jacobi Medical Center" on 84 of its tax IDs), so it's shown only under "Listed by the
    /// plan as …".
    /// </summary>
    internal const string UnknownPracticeName = "Unknown practice";

    /// <summary>Creates the macros, and empty tables when there are none, so queries never meet a missing one.</summary>
    public static Task EnsureAsync(DuckDBConnection connection, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(SqlFile.For<PracticeNames>("PracticeNameMacro.sql"), cancellationToken: cancellationToken));

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await RefreshAsync(connection, cancellationToken);
    }

    /// <summary>
    /// Rebuilds both tables. A failure is logged and leaves the previous names in place: lists then show the plan's names
    /// for groups saved since, and search keeps working.
    /// </summary>
    public async Task RefreshAsync(DuckDBConnection connection, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                SqlFile.For<PracticeNames>("NameChecks.sql"),
                new { comparableTaxIds = ComparableTaxIds, reliableAgreement = ReliableAgreement, filesToJudgeBy = FilesToJudgeBy },
                cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                SqlFile.For<PracticeNames>("PracticeNames.sql"),
                new { sharedTaxIds = SharedNameTaxIds, unknownName = UnknownPracticeName },
                cancellationToken: cancellationToken));
            var (names, unreliable) = await connection.QuerySingleAsync<(long, long)>(
                "SELECT (SELECT count(*) FROM practice_names), (SELECT count(*) FROM name_checks WHERE unreliable)");
            logger.LogInformation(
                "Reference: {Count} provider groups show their practice's own name; {Unreliable} rate files have unreliable names (built in {Ms} ms)",
                names, unreliable, stopwatch.ElapsedMilliseconds);
        }
        catch (DuckDBException ex)
        {
            logger.LogWarning(ex, "Reference: couldn't rebuild practice names; lists keep the names they had");
        }
    }
}
