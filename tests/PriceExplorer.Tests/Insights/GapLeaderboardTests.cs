using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static PriceExplorer.Tests.Insights.InsightFiles;

namespace PriceExplorer.Tests.Insights;

/// <summary>GET /api/v1/insights/gaps on the synthetic files, whose statistics are worked out in <see cref="InsightFiles"/>.</summary>
public sealed class GapLeaderboardTests(InsightsApiFactory factory) : IClassFixture<InsightsApiFactory>
{
    private static readonly string AmbetterColonoscopy = GroupKey(Ambetter, "CPT", "45378", "facility", "asc");
    private static readonly string AmbetterVisit = GroupKey(Ambetter, "CPT", "99213", "professional", "office");
    private static readonly string EssentialVisit = GroupKey(Essential, "CPT", "99213", "professional", "office");
    private static readonly string AmbetterBotox = GroupKey(Ambetter, "HCPCS", "J0585", "professional", "office");

    [Fact(Timeout = 60_000)]
    public async Task Groups_with_ten_or_more_providers_are_ranked_by_the_gap_to_the_lower_quartile()
    {
        var result = await GetAsync("/api/v1/insights/gaps", TestContext.Current.CancellationToken);

        Keys(result).Should().Equal(AmbetterColonoscopy, AmbetterVisit, EssentialVisit, AmbetterBotox);
        Items(result).Select(row => row.GetProperty("lowerQuartileGap").GetDecimal()).Should().Equal(112.50m, 15m, 10m, 2.25m);
        Items(result).Select(row => row.GetProperty("rank").GetInt32()).Should().Equal(1, 2, 3, 4);
        result.GetProperty("minimumProviders").GetInt32().Should().Be(10);

        var first = Items(result)[0];
        first.GetProperty("providerCount").GetInt32().Should().Be(10);
        first.GetProperty("categoryKey").GetString().Should().Be("surgery");
        first.GetProperty("feeTypeLabel").GetString().Should().Be("Facility fee");
        first.GetProperty("settingLabel").GetString().Should().Be("Ambulatory surgery center");
        first.GetProperty("codeName").ValueKind.Should().Be(JsonValueKind.Null, "45378 is only named in revenue-code lines, whose names aren't used");
        Items(result)[1].GetProperty("codeName").GetString().Should().Be("CPT 99213", "the plan's own item name");
        var stats = first.GetProperty("stats");
        (stats.GetProperty("p10").GetDecimal(), stats.GetProperty("p25").GetDecimal(), stats.GetProperty("median").GetDecimal(),
                stats.GetProperty("p90").GetDecimal(), stats.GetProperty("p90ToP10").GetDecimal())
            .Should().Be((445m, 512.50m, 625m, 930m, 2.09m));
    }

    [Fact(Timeout = 60_000)]
    public async Task Facility_charges_with_no_procedure_named_are_left_out_unless_asked_for()
    {
        var plain = await GetAsync("/api/v1/insights/gaps", TestContext.Current.CancellationToken);
        var all = await GetAsync("/api/v1/insights/gaps?includeFacilityCharges=true", TestContext.Current.CancellationToken);

        plain.GetProperty("facilityCharges").GetInt32().Should().Be(1);
        Keys(plain).Should().NotContain(key => key.Contains("|RC|", StringComparison.Ordinal));
        plain.GetProperty("notes").EnumerateArray().Select(note => note.GetString())
            .Should().Contain(note => note!.StartsWith("1 group of facility charges filed under a revenue code", StringComparison.Ordinal));

        all.GetProperty("query").GetProperty("includeFacilityCharges").GetBoolean().Should().BeTrue();
        var first = Items(all)[0];
        first.GetProperty("groupKey").GetString().Should().Be(GroupKey(Ambetter, "RC", "0360", "facility", "hospital-outpatient"));
        first.GetProperty("lowerQuartileGap").GetDecimal().Should().Be(225m);
        first.GetProperty("codeName").GetString().Should().Be("Operating room");
        Keys(all).Should().HaveCount(5);
        Categories(all).Should().Contain(("facility", 1));
        all.GetProperty("notes").GetArrayLength().Should().Be(1);
    }

    [Fact(Timeout = 60_000)]
    public async Task Spread_ranks_by_the_90th_over_the_10th_percentile()
    {
        var result = await GetAsync("/api/v1/insights/gaps?sort=spread", TestContext.Current.CancellationToken);

        Keys(result).Should().Equal(AmbetterBotox, AmbetterColonoscopy, EssentialVisit, AmbetterVisit);
        Items(result).Select(row => row.GetProperty("stats").GetProperty("p90ToP10").GetDecimal()).Should().Equal(5.84m, 2.09m, 1.84m, 1.75m);
        result.GetProperty("query").GetProperty("sort").GetString().Should().Be("spread");
    }

    [Theory(Timeout = 60_000)]
    [InlineData("product=essential-plan", new[] { "essential-plan|CPT|99213|professional|office||negotiated" })]
    [InlineData("feeType=facility", new[] { "ambetter-from-fidelis-care|CPT|45378|facility|asc||negotiated" })]
    [InlineData("setting=asc", new[] { "ambetter-from-fidelis-care|CPT|45378|facility|asc||negotiated" })]
    [InlineData("category=drugs", new[] { "ambetter-from-fidelis-care|HCPCS|J0585|professional|office||negotiated" })]
    [InlineData("code=99213", new[] { "ambetter-from-fidelis-care|CPT|99213|professional|office||negotiated", "essential-plan|CPT|99213|professional|office||negotiated" })]
    [InlineData("code=CPT%2099213&product=ambetter-from-fidelis-care", new[] { "ambetter-from-fidelis-care|CPT|99213|professional|office||negotiated" })]
    [InlineData("code=j0585", new[] { "ambetter-from-fidelis-care|HCPCS|J0585|professional|office||negotiated" })]
    [InlineData("code=99214", new string[0])]
    public async Task Filters_narrow_the_ranking(string filter, string[] keys)
    {
        var result = await GetAsync($"/api/v1/insights/gaps?{filter}", TestContext.Current.CancellationToken);

        Keys(result).Should().Equal(keys);
        result.GetProperty("rows").GetProperty("total").GetInt32().Should().Be(keys.Length);
    }

    [Fact(Timeout = 60_000)]
    public async Task Categories_count_their_groups_under_the_other_filters()
    {
        var all = await GetAsync("/api/v1/insights/gaps?category=drugs", TestContext.Current.CancellationToken);
        var office = await GetAsync("/api/v1/insights/gaps?setting=office", TestContext.Current.CancellationToken);

        Categories(all).Should().Equal(("visits", 2), ("drugs", 1), ("surgery", 1));
        Categories(office).Should().Equal(("visits", 2), ("drugs", 1));
    }

    [Fact(Timeout = 60_000)]
    public async Task Pages_continue_the_ranking_without_overlap()
    {
        var first = await GetAsync("/api/v1/insights/gaps?pageSize=3", TestContext.Current.CancellationToken);
        var second = await GetAsync("/api/v1/insights/gaps?pageSize=3&page=2", TestContext.Current.CancellationToken);
        var past = await GetAsync("/api/v1/insights/gaps?pageSize=3&page=9", TestContext.Current.CancellationToken);

        Keys(first).Concat(Keys(second)).Should().Equal(AmbetterColonoscopy, AmbetterVisit, EssentialVisit, AmbetterBotox);
        Items(second).Single().GetProperty("rank").GetInt32().Should().Be(4);
        second.GetProperty("rows").GetProperty("total").GetInt32().Should().Be(4);
        Items(past).Should().BeEmpty();
    }

    [Fact(Timeout = 60_000)]
    public async Task Each_row_carries_the_search_that_opens_its_group_and_shows_the_same_numbers()
    {
        using var client = factory.CreateClient();
        var result = await GetAsync("/api/v1/insights/gaps", TestContext.Current.CancellationToken);

        foreach (var row in Items(result))
        {
            var link = row.GetProperty("search").Deserialize<Dictionary<string, string>>()!;
            link.Keys.Should().BeEquivalentTo(["code", "codeType", "group"]);
            var search = await client.GetFromJsonAsync<JsonElement>(
                $"/api/v1/rates?{string.Join('&', link.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"))}",
                TestContext.Current.CancellationToken);

            var card = search.GetProperty("groups").EnumerateArray().Single(group => group.GetProperty("key").GetString() == link["group"]);
            Numbers(card.GetProperty("stats")).Should().Equal(Numbers(row.GetProperty("stats")));
            card.GetProperty("lowerQuartileGap").GetDecimal().Should().Be(row.GetProperty("lowerQuartileGap").GetDecimal());
            card.GetProperty("providerCount").GetInt32().Should().Be(row.GetProperty("providerCount").GetInt32());
            search.GetProperty("rows").GetProperty("items").EnumerateArray()
                .Should().OnlyContain(item => item.GetProperty("groupKey").GetString() == link["group"], "the link narrows the rows to the group");
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task The_response_names_its_snapshot_says_its_statistics_are_current_and_that_there_are_no_claim_counts()
    {
        var result = await GetAsync("/api/v1/insights/gaps", TestContext.Current.CancellationToken);

        var statistics = result.GetProperty("statistics");
        statistics.GetProperty("isCurrent").GetBoolean().Should().BeTrue();
        statistics.GetProperty("builtAt").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UnixEpoch);
        statistics.GetProperty("snapshot").GetProperty("indexDate").GetString().Should().Be("2026-08-31");
        result.GetProperty("notes").EnumerateArray().Select(note => note.GetString())
            .Should().ContainSingle(note => note!.Contains("no claim counts", StringComparison.Ordinal));
    }

    [Theory(Timeout = 60_000)]
    [InlineData("sort=savings", "sort")]
    [InlineData("feeType=institutional", "feeType")]
    [InlineData("setting=moon", "setting")]
    [InlineData("product=Essential%20Plan", "product")]
    [InlineData("category=Surgery%20%26%20procedures", "category")]
    [InlineData("code=4537%3B--", "code")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("page=0", "page")]
    public async Task Bad_filters_get_a_message_under_their_own_name(string filter, string field)
    {
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(TestContext.Current.CancellationToken);

        using var response = await client.GetAsync($"/api/v1/insights/gaps?{filter}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("code").GetString().Should().Be("validation_failed");
        problem.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    private async Task<JsonElement> GetAsync(string url, CancellationToken ct)
    {
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);
        return await client.GetFromJsonAsync<JsonElement>(url, ct);
    }

    private static List<JsonElement> Items(JsonElement result) => result.GetProperty("rows").GetProperty("items").EnumerateArray().ToList();

    private static List<string> Keys(JsonElement result) => Items(result).Select(row => row.GetProperty("groupKey").GetString()!).ToList();

    /// <summary>An object's numbers by name, compared as numbers (saved statistics carry 4 decimal places, search's 2).</summary>
    private static List<(string, decimal?)> Numbers(JsonElement stats) =>
        stats.EnumerateObject()
            .Select(property => (property.Name, property.Value.ValueKind == JsonValueKind.Number ? property.Value.GetDecimal() : (decimal?)null))
            .OrderBy(pair => pair.Name, StringComparer.Ordinal)
            .ToList();

    private static List<(string, int)> Categories(JsonElement result) =>
        result.GetProperty("categories").EnumerateArray()
            .Select(option => (option.GetProperty("key").GetString()!, option.GetProperty("count").GetInt32())).ToList();
}
