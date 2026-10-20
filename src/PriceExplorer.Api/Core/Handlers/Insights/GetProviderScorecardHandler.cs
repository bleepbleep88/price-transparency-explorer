using Dapper;
using MediatR;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Exceptions;
using PriceExplorer.Api.Core.Handlers.Rates;
using PriceExplorer.Api.Core.Models.Insights;
using PriceExplorer.Api.Core.Queries.Insights;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Api.Core.Handlers.Insights;

/// <summary>
/// A provider's saved value in every comparison group it is in, placed the way search places a row: percentile with
/// <see cref="RateStatistics.PercentRank(IReadOnlyList{decimal}, decimal, decimal)"/> over the group's saved values,
/// × median with <see cref="RateStatistics.Ratio"/>, band with <see cref="RateStatistics.Band"/>. Saved values are the
/// search's own (the statistics build runs search's code), so each group reads exactly as a search with this EIN shows it.
/// </summary>
internal sealed class GetProviderScorecardHandler(DuckDbConnectionFactory connections, ReferenceCatalog catalog)
    : IRequestHandler<GetProviderScorecardQuery, ProviderScorecard>
{
    /// <summary>How many groups the highest and lowest lists show.</summary>
    public const int Extremes = 5;

    public async Task<ProviderScorecard> Handle(GetProviderScorecardQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var snapshot = await SnapshotResolver.ResolveAsync(connection, request.Snapshot);

        // One transaction: the provider, its groups and their values all come from the same statistics build.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var providerKey = await connection.ExecuteScalarAsync<string?>(
                SqlFile.For<GetProviderScorecardHandler>("ResolveProvider.sql"),
                new { snapshotId = snapshot.SnapshotId, provider = ProviderKeys.NormalizeKey(request.Provider) },
                transaction)
            ?? throw new NotFoundException("No provider with this tax ID has rates in this snapshot.", "provider_not_found");
        var statistics = await InsightSupport.BasisAsync(connection, snapshot, transaction);

        var args = new { snapshotId = snapshot.SnapshotId, provider = providerKey };
        var rows = (await connection.QueryAsync<ScorecardGroupRow>(
            SqlFile.For<GetProviderScorecardHandler>("ScorecardGroups.sql"), args, transaction)).ToList();
        var peers = (await connection.QueryAsync<PeerValue>(SqlFile.For<GetProviderScorecardHandler>("ScorecardPeers.sql"), args, transaction))
            .GroupBy(peer => peer.GroupKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<decimal>)group.Select(peer => peer.Value).Order().ToList(), StringComparer.Ordinal);
        var who = await connection.QuerySingleAsync<ProviderRow>(
            SqlFile.For<GetProviderScorecardHandler>("ScorecardProvider.sql"), args, transaction);
        var names = await InsightSupport.CodeNamesAsync(connection, snapshot.SnapshotId, rows.Select(row => row.Code), transaction);

        var groups = rows
            .Select(row => Place(row, peers.GetValueOrDefault(row.GroupKey), names, snapshot, providerKey))
            .OrderBy(group => group.CodeType, StringComparer.Ordinal)
            .ThenBy(group => group.Code, StringComparer.Ordinal)
            .ThenBy(group => group.ProductLabel, StringComparer.Ordinal)
            .ThenBy(group => group.FeeType, StringComparer.Ordinal)
            .ThenBy(group => InsightSupport.SettingOrder(group.SettingKey, catalog))
            .ThenBy(group => group.GroupKey, StringComparer.Ordinal)
            .ToList();

        var withMedian = groups.Where(group => group.Median is > 0).ToList();
        var index = new PriceIndex(
            InsightMath.PriceIndex(groups.Select(group => group.VsMedian)),
            withMedian.Count,
            groups.Count,
            InsightMath.MinimumGroupsForIndex,
            RateStatistics.MinimumForBasicStats,
            withMedian.Count(group => group.Value < group.Median),
            withMedian.Count(group => group.Value == group.Median),
            withMedian.Count(group => group.Value > group.Median));

        // Furthest from the median by the exact ratio; ties go to the bigger group, then the group key.
        var highest = withMedian.Where(group => group.Value > group.Median)
            .OrderByDescending(group => group.Value / group.Median!.Value)
            .ThenByDescending(group => group.ProviderCount)
            .ThenBy(group => group.GroupKey, StringComparer.Ordinal)
            .Take(Extremes)
            .ToList();
        var lowest = withMedian.Where(group => group.Value < group.Median)
            .OrderBy(group => group.Value / group.Median!.Value)
            .ThenByDescending(group => group.ProviderCount)
            .ThenBy(group => group.GroupKey, StringComparer.Ordinal)
            .Take(Extremes)
            .ToList();

        return new ProviderScorecard(statistics, Describe(providerKey, who, rows), index, highest, lowest, groups);
    }

    private ScorecardGroup Place(
        ScorecardGroupRow row, IReadOnlyList<decimal>? peerValues, IReadOnlyDictionary<(string, string), string> names,
        ResolvedSnapshot snapshot, string providerKey)
    {
        // Exactly the row arithmetic of RateSearchAssembler: a percentile and a band only with 5+ providers (a median).
        var basic = row.ProviderCount >= RateStatistics.MinimumForBasicStats && peerValues is { Count: > 0 };
        var category = ServiceCategories.Classify(row.CodeType, row.Code);
        return new ScorecardGroup(
            GroupKey: row.GroupKey,
            ProductKey: row.ProductKey,
            ProductLabel: row.ProductLabel,
            CodeType: row.CodeType,
            Code: row.Code,
            CodeName: InsightSupport.NameOf(names, row.CodeType, row.Code),
            CategoryKey: category.Key,
            CategoryLabel: category.Label,
            FeeType: row.FeeType,
            FeeTypeLabel: RateSearchAssembler.FeeTypeLabelOf(row.FeeType),
            SettingKey: row.SettingKey,
            SettingLabel: InsightSupport.SettingLabel(row.SettingKey, catalog),
            Modifiers: InsightSupport.Modifiers(row.Modifiers, catalog),
            NegotiatedType: row.NegotiatedType,
            Unit: row.Unit,
            PerUnit: row.PerUnit,
            ProviderCount: row.ProviderCount,
            StatsLevel: row.StatsLevel,
            Median: row.Median,
            Value: row.Value,
            RateLow: row.Low,
            RateHigh: row.High,
            Percentile: basic ? RateStatistics.PercentRank(peerValues!, row.Value, row.Value) : null,
            VsMedian: RateStatistics.Ratio(row.Value, row.Median),
            Band: basic && row is { P25: { } p25, Median: { } median, P75: { } p75 } ? RateStatistics.Band(row.Value, p25, median, p75) : null,
            Search: InsightSupport.SearchLink(snapshot, row.CodeType, row.Code, row.GroupKey, providerKey));
    }

    /// <summary>
    /// The tax ID as results show it (masked when the IRS never issues its prefix), the name on most of its groups first,
    /// and its products by how many groups each has.
    /// </summary>
    private static ScorecardProvider Describe(string providerKey, ProviderRow who, IReadOnlyList<ScorecardGroupRow> rows)
    {
        var listed = (who.Names ?? "").Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var name = rows
            .GroupBy(row => row.BusinessName, StringComparer.Ordinal)
            .OrderByDescending(same => same.Count())
            .ThenBy(same => same.Key, StringComparer.Ordinal)
            .Select(same => same.Key)
            .FirstOrDefault() ?? listed.FirstOrDefault() ?? "Name not published";
        var products = rows
            .GroupBy(row => (row.ProductKey, row.ProductLabel))
            .Select(same => new ProductGroups(same.Key.ProductKey, same.Key.ProductLabel, same.Count()))
            .OrderByDescending(product => product.Groups)
            .ThenBy(product => product.Label, StringComparer.Ordinal)
            .ToList();

        return new ScorecardProvider(
            providerKey,
            who.EinUnverified ? null : who.Ein,
            ProviderKeys.Display(providerKey, who.Ein, who.EinUnverified),
            who.EinUnverified,
            name,
            listed.Where(other => other != name).ToList(),
            (int)who.NpiCount,
            who.WholeTaxId,
            products);
    }

    private sealed record ScorecardGroupRow(
        string GroupKey, string ProductKey, string ProductLabel, string CodeType, string Code, string FeeType, string SettingKey,
        string Modifiers, string NegotiatedType, string Unit, bool PerUnit, int ProviderCount, string StatsLevel, decimal? P25,
        decimal? Median, decimal? P75, decimal Value, decimal Low, decimal High, string BusinessName, string? Ein,
        string EinDisplay, bool EinMasked);

    private sealed record PeerValue(string GroupKey, decimal Value);

    private sealed record ProviderRow(string? Ein, bool EinUnverified, bool WholeTaxId, string? Names, long NpiCount);
}
