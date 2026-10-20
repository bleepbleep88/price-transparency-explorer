using Dapper;
using MediatR;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Models.Statistics;
using PriceExplorer.Api.Core.Queries.Statistics;

namespace PriceExplorer.Api.Core.Handlers.Statistics;

/// <summary>Reads a snapshot's saved statistics, written by <see cref="Ingestion.SnapshotStatisticsBuilder"/>.</summary>
internal sealed class GetGroupStatsHandler(DuckDbConnectionFactory connections) : IRequestHandler<GetGroupStatsQuery, GroupStatsResult>
{
    public async Task<GroupStatsResult> Handle(GetGroupStatsQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var snapshot = await SnapshotResolver.ResolveAsync(connection, request.Snapshot);
        var filters = new
        {
            snapshotId = snapshot.SnapshotId,
            codes = request.Codes is null ? null : string.Join(',', request.Codes.Select(CodeInput.Clean)),
            provider = ProviderKeys.NormalizeKey(request.ProviderKey),
        };

        // One transaction, so the groups, the values and whether they're current all come from the same moment, even when
        // a rebuild or a file reload commits meanwhile.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var state = (await SavedStatistics.StateAsync(connection, snapshot.SnapshotId, transaction)).SingleOrDefault();
        var groups = await connection.QueryAsync<GroupRow>(SqlFile.For<GetGroupStatsHandler>("GroupStats.sql"), filters, transaction);
        var values = request.IncludeProviders
            ? (await connection.QueryAsync<ValueRow>(SqlFile.For<GetGroupStatsHandler>("ProviderValues.sql"), filters, transaction))
                .ToLookup(row => row.GroupKey, StringComparer.Ordinal)
            : null;

        return new GroupStatsResult(
            new SnapshotRef(snapshot.SnapshotId, snapshot.IndexDate, snapshot.IsLatest),
            UtcTimestamps.From(state?.BuiltAt),
            state?.Current ?? false,
            state?.ExpiresOn,
            groups.Select(group => new GroupStats(
                group.GroupKey, group.ProductKey, group.ProductLabel, group.CodeType, group.Code, group.FeeType, group.SettingKey,
                group.Modifiers, group.NegotiatedType, group.Unit, group.PerUnit, group.ProviderCount, group.RateCount, group.StatsLevel,
                new RateStats(group.Min, group.P10, group.P25, group.Median, group.P75, group.P90, group.Max, group.SpreadMultiple, group.P90ToP10),
                group.LowerQuartileGap,
                values is null
                    ? []
                    : values[group.GroupKey].Select(value => new ProviderValue(
                        value.ProviderKey, value.Ein, value.EinDisplay, value.EinMasked, value.EinLevel, value.BusinessName,
                        value.Value, value.Low, value.High, value.RateCount, value.NpiWeight, value.FirstRateId,
                        value.WholeTaxIdRateId, value.RateSet)).ToList()))
                .ToList());
    }

    private sealed record GroupRow(
        string GroupKey, string ProductKey, string ProductLabel, string CodeType, string Code, string FeeType, string SettingKey,
        string Modifiers, string NegotiatedType, string Unit, bool PerUnit, int ProviderCount, int RateCount, string StatsLevel,
        decimal Min, decimal? P10, decimal? P25, decimal? Median, decimal? P75, decimal? P90, decimal Max, decimal? SpreadMultiple,
        decimal? P90ToP10, decimal? LowerQuartileGap);

    private sealed record ValueRow(
        string GroupKey, string ProviderKey, string? Ein, string EinDisplay, bool EinMasked, bool EinLevel, string BusinessName,
        decimal Value, decimal Low, decimal High, int RateCount, long NpiWeight, long FirstRateId, long? WholeTaxIdRateId,
        string RateSet);
}
