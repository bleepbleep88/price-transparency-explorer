using System.Diagnostics;
using Dapper;
using DuckDB.NET.Data;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Handlers.Rates;
using PriceExplorer.Api.Core.Models.Statistics;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Api.Core.Ingestion;

/// <summary>
/// Saves every comparison group's statistics (group_stats) and every provider's value (provider_values) for a snapshot,
/// so history and insights can read them instead of searching each code. They come from the search's own code:
/// SearchRates.sql with no provider filters, then <see cref="RateSearchAssembler.Summarize"/>, so they match
/// GET /api/v1/rates by construction (same weighted median, setting mapping and rounding).
/// </summary>
internal sealed class SnapshotStatisticsBuilder(DuckDbConnectionFactory connections, ReferenceCatalog catalog, TimeProvider time)
{
    /// <summary>
    /// Codes are searched a batch at a time, never splitting a code, so memory stays modest. The 2026-08-31 snapshot has
    /// 17,549 rates under 1,631 codes, which SearchRates.sql turns into about 66K facts (7.2K for its largest code).
    /// </summary>
    internal const int RatesPerBatch = 2_000;

    /// <summary>Rates per batch; tests set fewer so the small fixtures build in several batches.</summary>
    internal int BatchRates { get; init; } = RatesPerBatch;

    /// <summary>
    /// Snapshots search can serve whose saved statistics aren't current: missing, or built from other rate files, by
    /// another build of the app, or including a rate that has expired since (see <see cref="SavedStatistics"/>).
    /// </summary>
    public async Task<IReadOnlyList<StatisticsState>> FindStaleAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return (await SavedStatistics.StateAsync(connection)).Where(state => !state.Current).ToList();
    }

    /// <summary>
    /// Replaces the snapshot's statistics in one transaction: readers see the previous ones until it commits, and keep
    /// them if the build stops. <paramref name="onProgress"/> gets the codes done and the total, before and after each batch.
    /// </summary>
    /// <param name="snapshot">The snapshot as <see cref="FindStaleAsync"/> read it, before any rate was read for the build,
    /// so a rate file saved meanwhile leaves the build recorded as not current.</param>
    public async Task<StatisticsBuild> BuildAsync(StatisticsState snapshot, Action<int, int> onProgress, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var args = new { snapshotId = snapshot.SnapshotId };
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var codes = (await connection.QueryAsync<CodeRates>(
            SqlFile.For<SnapshotStatisticsBuilder>("SnapshotCodes.sql"), args, transaction)).ToList();
        await connection.ExecuteAsync("DELETE FROM group_stats WHERE snapshot_id = $snapshotId", args, transaction);
        await connection.ExecuteAsync("DELETE FROM provider_values WHERE snapshot_id = $snapshotId", args, transaction);

        var (done, groups, values) = (0, 0, 0);
        onProgress(done, codes.Count);
        foreach (var batch in Batches(codes, BatchRates))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var facts = await SearchRatesHandler.QueryFactsAsync(connection, snapshot.SnapshotId, batch, transaction: transaction);
            var summaries = RateSearchAssembler.Summarize(facts, catalog);
            Append(connection, snapshot.SnapshotId, summaries);

            done += batch.Count;
            groups += summaries.Count;
            values += summaries.Sum(group => group.Providers.Count);
            onProgress(done, codes.Count);
        }

        stopwatch.Stop();
        await connection.ExecuteAsync("DELETE FROM stats_builds WHERE snapshot_id = $snapshotId", args, transaction);
        await connection.ExecuteAsync(
            SqlFile.For<SnapshotStatisticsBuilder>("SaveStatisticsBuild.sql"),
            new
            {
                snapshotId = snapshot.SnapshotId,
                rateFiles = snapshot.RateFiles,
                codeVersion = SavedStatistics.CodeVersion,
                builtAt = time.GetUtcNow().UtcDateTime,
                buildMs = (int)stopwatch.ElapsedMilliseconds,
                codeCount = codes.Count,
                groupCount = groups,
                valueCount = values,
            },
            transaction);
        await transaction.CommitAsync(cancellationToken);

        return new StatisticsBuild(codes.Count, groups, values, stopwatch.Elapsed);
    }

    /// <summary>Codes in batches of about <paramref name="ratesPerBatch"/> rates. A code is never split: a larger one gets a batch of its own.</summary>
    internal static IEnumerable<List<string>> Batches(IEnumerable<CodeRates> codes, int ratesPerBatch = RatesPerBatch)
    {
        var batch = new List<string>();
        var rates = 0L;
        foreach (var code in codes)
        {
            if (batch.Count > 0 && rates + code.Rates > ratesPerBatch)
            {
                yield return batch;
                batch = [];
                rates = 0;
            }

            batch.Add(code.Code);
            rates += code.Rates;
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }

    private static void Append(DuckDBConnection connection, int snapshotId, IReadOnlyList<GroupStats> groups)
    {
        using (var appender = connection.CreateAppender("group_stats"))
        {
            foreach (var group in groups)
            {
                appender.CreateRow()
                    .AppendValue(snapshotId)
                    .AppendValue(group.Key)
                    .AppendValue(group.ProductKey)
                    .AppendValue(group.ProductLabel)
                    .AppendValue(group.CodeType)
                    .AppendValue(group.Code)
                    .AppendValue(group.FeeType)
                    .AppendValue(group.SettingKey)
                    .AppendValue(group.Modifiers)
                    .AppendValue(group.NegotiatedType)
                    .AppendValue(group.Unit)
                    .AppendValue(group.PerUnit)
                    .AppendValue(group.ProviderCount)
                    .AppendValue(group.RateCount)
                    .AppendValue(group.StatsLevel)
                    .AppendValue(group.Stats.Min)
                    .AppendNumber(group.Stats.P10)
                    .AppendNumber(group.Stats.P25)
                    .AppendNumber(group.Stats.Median)
                    .AppendNumber(group.Stats.P75)
                    .AppendNumber(group.Stats.P90)
                    .AppendValue(group.Stats.Max)
                    .AppendNumber(group.Stats.SpreadMultiple)
                    .AppendNumber(group.Stats.P90ToP10)
                    .AppendNumber(group.LowerQuartileGap)
                    .EndRow();
            }
        }

        using var values = connection.CreateAppender("provider_values");
        foreach (var group in groups)
        {
            foreach (var provider in group.Providers)
            {
                values.CreateRow()
                    .AppendValue(snapshotId)
                    .AppendValue(group.Key)
                    .AppendValue(group.ProductKey)
                    .AppendValue(group.CodeType)
                    .AppendValue(group.Code)
                    .AppendValue(group.FeeType)
                    .AppendValue(group.SettingKey)
                    .AppendValue(group.Modifiers)
                    .AppendValue(group.NegotiatedType)
                    .AppendValue(provider.ProviderKey)
                    .AppendText(provider.Ein)
                    .AppendValue(provider.EinDisplay)
                    .AppendValue(provider.EinMasked)
                    .AppendValue(provider.EinLevel)
                    .AppendValue(provider.BusinessName)
                    .AppendValue(provider.Value)
                    .AppendValue(provider.Low)
                    .AppendValue(provider.High)
                    .AppendValue(provider.RateCount)
                    .AppendValue(provider.NpiWeight)
                    .AppendValue(provider.FirstRateId)
                    .AppendNumber(provider.WholeTaxIdRateId)
                    .AppendValue(provider.RateSet)
                    .EndRow();
            }
        }
    }
}

internal sealed record CodeRates(string Code, long Rates);

internal sealed record StatisticsBuild(int Codes, int Groups, int ProviderValues, TimeSpan Elapsed);
