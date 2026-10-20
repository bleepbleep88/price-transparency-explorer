using Dapper;
using MediatR;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Handlers.Rates;
using PriceExplorer.Api.Core.Models.History;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Queries.History;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Api.Core.Handlers.History;

/// <summary>
/// Runs the search itself in every snapshot with saved rates. The selected snapshot's results decide which groups are
/// listed (the cards on screen); each snapshot's own results give that group's numbers there and the searched provider's
/// rate. So every point equals what that snapshot's search shows, an NPI keeps its own rate, and nothing depends on saved
/// statistics being rebuilt.
/// </summary>
internal sealed class GetRateTrendHandler(DuckDbConnectionFactory connections, ReferenceCatalog catalog)
    : IRequestHandler<GetRateTrendQuery, RateTrendResult>
{
    public async Task<RateTrendResult> Handle(GetRateTrendQuery request, CancellationToken cancellationToken)
    {
        var search = SearchContext.From(request.AsSearch());
        await using var connection = await connections.OpenAsync(cancellationToken);
        var selected = await SnapshotResolver.ResolveAsync(connection, request.Snapshot);
        var latestId = await connection.ExecuteScalarAsync<int?>(SqlFile.For<DuckDbConnectionFactory>("LatestSnapshot.sql"));
        var snapshots = (await connection.QueryAsync<SnapshotRow>(SqlFile.For<GetRateTrendHandler>("TrendSnapshots.sql")))
            .Select(row => new ResolvedSnapshot(row.SnapshotId, row.IndexDate, row.SnapshotId == latestId))
            .ToList();
        var completeness = await Completeness.ReadAsync(connection);

        var groupsBySnapshot = new List<(ResolvedSnapshot Snapshot, Dictionary<string, RateGroup> Groups, Dictionary<string, Dictionary<string, ProviderRates>> Values)>();
        IReadOnlyList<RateGroup> shown = [];
        foreach (var snapshot in snapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (facts, assembled) = await SearchRatesHandler.RunAsync(connection, snapshot, search, "rate", page: 1, pageSize: 1, catalog);
            // Every provider's value per group, as the statistics count it (the same summary the saved statistics hold).
            var values = RateSearchAssembler.Summarize(facts, catalog).ToDictionary(
                group => group.Key,
                group => group.Providers.ToDictionary(
                    provider => provider.ProviderKey, provider => new ProviderRates(provider.Value, provider.RateSet), StringComparer.Ordinal),
                StringComparer.Ordinal);
            groupsBySnapshot.Add((snapshot, assembled.AllGroups.ToDictionary(group => group.Key, StringComparer.Ordinal), values));
            if (snapshot.SnapshotId == selected.SnapshotId)
            {
                shown = assembled.Groups;
            }
        }

        return new RateTrendResult(
            new SnapshotRef(selected.SnapshotId, selected.IndexDate, selected.IsLatest),
            snapshots.Select(snapshot => new SnapshotRef(snapshot.SnapshotId, snapshot.IndexDate, snapshot.IsLatest)).ToList(),
            shown.Select(group => new GroupTrend(group.Key, group.ProductKey, Points(group.Key, groupsBySnapshot, completeness))).ToList());
    }

    /// <summary>The group in every snapshot that has it, each complete point with how its providers moved since the last.</summary>
    private static List<TrendPoint> Points(
        string key,
        IEnumerable<(ResolvedSnapshot Snapshot, Dictionary<string, RateGroup> Groups, Dictionary<string, Dictionary<string, ProviderRates>> Values)> snapshots,
        IReadOnlyDictionary<(int SnapshotId, string ProductKey), ProductCompleteness> completeness)
    {
        var points = new List<TrendPoint>();
        (int SnapshotId, Dictionary<string, ProviderRates> Values)? previous = null;
        foreach (var (snapshot, groups, values) in snapshots)
        {
            if (!groups.TryGetValue(key, out var group))
            {
                continue;
            }

            var point = Point(snapshot, group, completeness);
            if (point.Partial)
            {
                points.Add(point);
                continue;
            }

            var current = values.GetValueOrDefault(key) ?? [];
            points.Add(previous is { } last ? point with { SincePrevious = Step(last.SnapshotId, last.Values, current) } : point);
            previous = (snapshot.SnapshotId, current);
        }

        return points;
    }

    /// <summary>
    /// Who stayed, who moved and who came or went, counting a rate as changed only when the file lists a different one (as
    /// ChangePairs.sql does): the same rates with another value only mean the NPI counts behind them moved.
    /// </summary>
    internal static TrendStep Step(
        int sinceSnapshotId, IReadOnlyDictionary<string, ProviderRates> before, IReadOnlyDictionary<string, ProviderRates> after)
    {
        var stayed = before.Keys.Where(after.ContainsKey).Select(provider => (Before: before[provider], After: after[provider])).ToList();
        var repriced = stayed.Where(pair => pair.After.RateSet != pair.Before.RateSet).ToList();
        return new TrendStep(
            sinceSnapshotId,
            stayed.Count,
            repriced.Count(pair => pair.After.Value > pair.Before.Value),
            repriced.Count(pair => pair.After.Value < pair.Before.Value),
            stayed.Count(pair => pair.After.RateSet == pair.Before.RateSet && pair.After.Value != pair.Before.Value),
            before.Count - stayed.Count,
            after.Count - stayed.Count);
    }

    /// <summary>A group as one snapshot's search card shows it; the provider is the one its card marks (the first highlight).</summary>
    private static TrendPoint Point(
        ResolvedSnapshot snapshot, RateGroup group, IReadOnlyDictionary<(int SnapshotId, string ProductKey), ProductCompleteness> completeness) =>
        new(
            snapshot.SnapshotId,
            snapshot.IndexDate,
            completeness.GetValueOrDefault((snapshot.SnapshotId, group.ProductKey))?.Partial ?? false,
            group.ProviderCount,
            group.StatsLevel,
            group.Stats.Min,
            group.Stats.P10,
            group.Stats.P25,
            group.Stats.Median,
            group.Stats.P75,
            group.Stats.P90,
            group.Stats.Max,
            group.Highlights.FirstOrDefault() is { } marked ? new TrendProvider(marked.ProviderKey, marked.DisplayName, marked.Rate) : null,
            SincePrevious: null);

    private sealed record SnapshotRow(int SnapshotId, DateOnly? IndexDate);
}

/// <summary>A provider's value in a group and the set of rates behind it (provider_values.rate_set).</summary>
internal readonly record struct ProviderRates(decimal Value, string RateSet);
