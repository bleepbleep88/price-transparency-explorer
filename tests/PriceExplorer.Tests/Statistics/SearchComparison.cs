using System.Net.Http.Json;
using System.Text.Json;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Models.Statistics;
using PriceExplorer.Api.Core.Queries.Statistics;
using PriceExplorer.Tests.Support;

namespace PriceExplorer.Tests.Statistics;

/// <summary>Compares a code's saved statistics with what GET /api/v1/rates returns for it, on every page.</summary>
internal static class SearchComparison
{
    /// <summary>
    /// Asserts the saved groups equal the search's groups (keys, counts, every statistic) and the saved provider values
    /// equal the search's rows (value, range, source rate, name, tax ID), with nothing extra. Returns what was saved.
    /// </summary>
    public static async Task<GroupStatsResult> SavedEqualsSearchAsync(ExplorerApiFactory factory, string code, CancellationToken ct)
    {
        var (groups, rows) = await SearchAllPagesAsync(factory, code, ct);

        var saved = await factory.SendAsync(new GetGroupStatsQuery(Codes: [code]), ct);

        groups.Should().NotBeEmpty();
        saved.BuiltAt.Should().NotBeNull();
        saved.Current.Should().BeTrue();
        saved.Groups.Select(GroupFacts.Of).Should().BeEquivalentTo(groups.Select(GroupFacts.Of));
        saved.Groups.SelectMany(group => group.Providers.Select(value => RowFacts.Of(group.Key, value)))
            .Should().BeEquivalentTo(rows.Select(RowFacts.Of), "every row on every page, and nothing else");
        return saved;
    }

    /// <summary>The groups (the same on every page) and every row on every page of an unfiltered search.</summary>
    private static async Task<(List<JsonElement> Groups, List<JsonElement> Rows)> SearchAllPagesAsync(
        ExplorerApiFactory factory, string code, CancellationToken ct)
    {
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);

        var first = await PageAsync(1);
        var total = first.GetProperty("rows").GetProperty("total").GetInt32();
        var rows = Items(first);
        for (var page = 2; rows.Count < total; page++)
        {
            var items = Items(await PageAsync(page));
            items.Should().NotBeEmpty("pages before the total is reached have rows");
            rows.AddRange(items);
        }

        return (first.GetProperty("groups").EnumerateArray().ToList(), rows);

        // Small pages, so the comparison covers paging too.
        Task<JsonElement> PageAsync(int page) =>
            client.GetFromJsonAsync<JsonElement>($"/api/v1/rates?code={code}&pageSize=10&page={page}", ct);

        static List<JsonElement> Items(JsonElement result) => result.GetProperty("rows").GetProperty("items").EnumerateArray().ToList();
    }

    private static decimal? Number(JsonElement parent, string name) =>
        parent.GetProperty(name) is { ValueKind: JsonValueKind.Number } number ? number.GetDecimal() : null;

    private sealed record GroupFacts(
        string Key, string ProductKey, string ProductLabel, string CodeType, string Code, string FeeType, string SettingKey,
        string Modifiers, string NegotiatedType, string Unit, bool PerUnit, int ProviderCount, int RateCount, string StatsLevel,
        RateStats Stats, decimal? LowerQuartileGap)
    {
        public static GroupFacts Of(GroupStats group) => new(
            group.Key, group.ProductKey, group.ProductLabel, group.CodeType, group.Code, group.FeeType, group.SettingKey,
            group.Modifiers, group.NegotiatedType, group.Unit, group.PerUnit, group.ProviderCount, group.RateCount, group.StatsLevel,
            group.Stats, group.LowerQuartileGap);

        public static GroupFacts Of(JsonElement group)
        {
            var stats = group.GetProperty("stats");
            return new GroupFacts(
                group.GetProperty("key").GetString()!,
                group.GetProperty("productKey").GetString()!,
                group.GetProperty("productLabel").GetString()!,
                group.GetProperty("codeType").GetString()!,
                group.GetProperty("code").GetString()!,
                group.GetProperty("feeType").GetString()!,
                group.GetProperty("settingKey").GetString()!,
                string.Join(',', group.GetProperty("modifiers").EnumerateArray().Select(modifier => modifier.GetProperty("code").GetString())),
                group.GetProperty("negotiatedType").GetString()!,
                group.GetProperty("unit").GetString()!,
                group.GetProperty("perUnit").GetBoolean(),
                group.GetProperty("providerCount").GetInt32(),
                group.GetProperty("rateCount").GetInt32(),
                group.GetProperty("statsLevel").GetString()!,
                new RateStats(
                    stats.GetProperty("min").GetDecimal(), Number(stats, "p10"), Number(stats, "p25"), Number(stats, "median"),
                    Number(stats, "p75"), Number(stats, "p90"), stats.GetProperty("max").GetDecimal(), Number(stats, "spreadMultiple"),
                    Number(stats, "p90ToP10")),
                Number(group, "lowerQuartileGap"));
        }
    }

    private sealed record RowFacts(
        string GroupKey, string ProviderKey, string? Ein, string EinDisplay, bool EinMasked, bool EinLevel, string BusinessName,
        decimal Value, decimal Low, decimal High, int RateCount, long FirstRateId, long? WholeTaxIdRateId)
    {
        public static RowFacts Of(string groupKey, ProviderValue value) => new(
            groupKey, value.ProviderKey, value.Ein, value.EinDisplay, value.EinMasked, value.EinLevel, value.BusinessName,
            value.Value, value.Low, value.High, value.RateCount, value.FirstRateId, value.WholeTaxIdRateId);

        public static RowFacts Of(JsonElement row) => new(
            row.GetProperty("groupKey").GetString()!,
            row.GetProperty("providerKey").GetString()!,
            row.GetProperty("ein").GetString(),
            row.GetProperty("einDisplay").GetString()!,
            row.GetProperty("einMasked").GetBoolean(),
            row.GetProperty("einLevel").GetBoolean(),
            row.GetProperty("businessName").GetString()!,
            row.GetProperty("rate").GetDecimal(),
            row.GetProperty("rateLow").GetDecimal(),
            row.GetProperty("rateHigh").GetDecimal(),
            row.GetProperty("rateCount").GetInt32(),
            row.GetProperty("firstRateId").GetInt64(),
            row.GetProperty("wholeTaxIdRateId") is { ValueKind: JsonValueKind.Number } rate ? rate.GetInt64() : null);
    }
}
