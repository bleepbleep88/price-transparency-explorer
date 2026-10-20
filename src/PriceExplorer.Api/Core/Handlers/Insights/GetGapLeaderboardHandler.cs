using Dapper;
using MediatR;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Handlers.Rates;
using PriceExplorer.Api.Core.Models.Insights;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Queries.Insights;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Api.Core.Handlers.Insights;

/// <summary>
/// Ranks the saved comparison groups with full statistics by the gap to the lower quartile or by spread. Reads
/// group_stats only, so every number is the one the group's search card shows.
/// </summary>
internal sealed class GetGapLeaderboardHandler(DuckDbConnectionFactory connections, ReferenceCatalog catalog, StatisticsBasisReader basis)
    : IRequestHandler<GetGapLeaderboardQuery, GapLeaderboard>
{
    public const string NoClaimCounts =
        "The files have no claim counts, so a gap is per case (or per unit for drugs and supplies), never a yearly total.";

    public async Task<GapLeaderboard> Handle(GetGapLeaderboardQuery request, CancellationToken cancellationToken)
    {
        var sort = request.Sort ?? "gap";
        var (codes, codeType) = InsightInput.Codes(request.Code);
        var category = InsightSupport.Clean(request.Category);

        await using var connection = await connections.OpenAsync(cancellationToken);
        var snapshot = await SnapshotResolver.ResolveAsync(connection, request.Snapshot);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var statistics = await basis.ReadAsync(connection, snapshot, transaction, cancellationToken);

        var groups = (await connection.QueryAsync<GapGroupRow>(
                SqlFile.For<GetGapLeaderboardHandler>("GapLeaderboard.sql"),
                new
                {
                    snapshotId = snapshot.SnapshotId,
                    product = InsightSupport.Clean(request.Product),
                    setting = InsightSupport.Clean(request.Setting),
                    feeType = InsightSupport.Clean(request.FeeType),
                    codes = InsightSupport.CodeList(codes),
                    codeType,
                },
                transaction))
            .Select(group => (Group: group, Category: ServiceCategories.Classify(group.CodeType, group.Code)))
            .ToList();

        // Each category's count is under the other filters, so the reader sees what picking it would leave.
        var categories = groups
            .GroupBy(entry => entry.Category.Key, StringComparer.Ordinal)
            .Select(same => new CategoryOption(same.Key, same.First().Category.Label, same.Count()))
            .OrderByDescending(option => option.Count)
            .ThenBy(option => option.Label, StringComparer.Ordinal)
            .ToList();

        var ranked = Rank(groups.Where(entry => category is null || entry.Category.Key == category), sort).ToList();
        var skip = (long)(request.Page - 1) * request.PageSize;
        var page = skip >= ranked.Count ? [] : ranked.Skip((int)skip).Take(request.PageSize).ToList();
        var names = await InsightSupport.CodeNamesAsync(connection, snapshot.SnapshotId, page.Select(entry => entry.Group.Code), transaction);

        var rows = page.Select((entry, index) =>
        {
            var group = entry.Group;
            return new GapRow(
                Rank: (int)skip + index + 1,
                GroupKey: group.GroupKey,
                ProductKey: group.ProductKey,
                ProductLabel: group.ProductLabel,
                CodeType: group.CodeType,
                Code: group.Code,
                CodeName: InsightSupport.NameOf(names, group.CodeType, group.Code),
                CategoryKey: entry.Category.Key,
                CategoryLabel: entry.Category.Label,
                FeeType: group.FeeType,
                FeeTypeLabel: RateSearchAssembler.FeeTypeLabelOf(group.FeeType),
                SettingKey: group.SettingKey,
                SettingLabel: InsightSupport.SettingLabel(group.SettingKey, catalog),
                Modifiers: InsightSupport.Modifiers(group.Modifiers, catalog),
                NegotiatedType: group.NegotiatedType,
                Unit: group.Unit,
                PerUnit: group.PerUnit,
                ProviderCount: group.ProviderCount,
                Stats: new RateStats(group.Min, group.P10, group.P25, group.Median, group.P75, group.P90, group.Max, group.SpreadMultiple, group.P90ToP10),
                LowerQuartileGap: group.LowerQuartileGap,
                Search: InsightSupport.SearchLink(snapshot, group.CodeType, group.Code, group.GroupKey));
        }).ToList();

        return new GapLeaderboard(
            statistics,
            new GapLeaderboardEcho(
                InsightSupport.Clean(request.Product), category, InsightSupport.Clean(request.Setting), InsightSupport.Clean(request.FeeType),
                InsightSupport.Clean(request.Code), codes, sort, request.Page, request.PageSize),
            RateStatistics.MinimumForFullStats,
            categories,
            new GapRowsPage(ranked.Count, request.Page, request.PageSize, rows),
            [NoClaimCounts]);
    }

    /// <summary>Largest first. Ties go to the group with more providers, then the group key, so pages never overlap.</summary>
    private static IEnumerable<(GapGroupRow Group, ServiceCategory Category)> Rank(
        IEnumerable<(GapGroupRow Group, ServiceCategory Category)> groups, string sort) =>
        (sort == "spread"
            ? groups.OrderByDescending(entry => entry.Group.P90ToP10).ThenByDescending(entry => entry.Group.LowerQuartileGap)
            : groups.OrderByDescending(entry => entry.Group.LowerQuartileGap).ThenByDescending(entry => entry.Group.P90ToP10))
        .ThenByDescending(entry => entry.Group.ProviderCount)
        .ThenBy(entry => entry.Group.GroupKey, StringComparer.Ordinal);

    private sealed record GapGroupRow(
        string GroupKey, string ProductKey, string ProductLabel, string CodeType, string Code, string FeeType, string SettingKey,
        string Modifiers, string NegotiatedType, string Unit, bool PerUnit, int ProviderCount, decimal Min, decimal? P10, decimal? P25,
        decimal? Median, decimal? P75, decimal? P90, decimal Max, decimal? SpreadMultiple, decimal? P90ToP10, decimal LowerQuartileGap);
}
