using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static PriceExplorer.Tests.Insights.InsightFiles;

namespace PriceExplorer.Tests.Insights;

/// <summary>GET /api/v1/insights/compare on the synthetic files (see <see cref="InsightFiles"/>).</summary>
public sealed class ProductComparisonTests(InsightsApiFactory factory) : IClassFixture<InsightsApiFactory>
{
    [Fact(Timeout = 60_000)]
    public async Task By_default_the_second_product_by_name_is_compared_with_the_first()
    {
        var result = await GetAsync("/api/v1/insights/compare", TestContext.Current.CancellationToken);

        result.GetProperty("product").GetProperty("key").GetString().Should().Be(Essential);
        result.GetProperty("versus").GetProperty("key").GetString().Should().Be(Ambetter);
        result.GetProperty("products").GetArrayLength().Should().Be(2);
    }

    [Fact(Timeout = 60_000)]
    public async Task Pairs_are_counted_as_identical_lower_or_higher_with_the_ratios_quantiles()
    {
        var summary = (await GetAsync("/api/v1/insights/compare", TestContext.Current.CancellationToken)).GetProperty("summary");

        // 99213: 5 the same, A04 0.9, A05 0.5, A10 0.98 lower, A06 1.1, A08 2.0 higher; 45378: 5 the same.
        (summary.GetProperty("pairs").GetInt32(), summary.GetProperty("identical").GetInt32(), summary.GetProperty("lower").GetInt32(),
                summary.GetProperty("higher").GetInt32(), summary.GetProperty("providers").GetInt32(), summary.GetProperty("codes").GetInt32())
            .Should().Be((15, 10, 3, 2, 10, 2));
        new[] { "p10", "p25", "median", "p75", "p90" }.Select(name => summary.GetProperty(name).GetDecimal())
            .Should().Equal(0.93m, 1m, 1m, 1m, 1.06m);
    }

    [Fact(Timeout = 60_000)]
    public async Task The_distribution_puts_each_pair_in_one_band()
    {
        var result = await GetAsync("/api/v1/insights/compare", TestContext.Current.CancellationToken);

        result.GetProperty("distribution").EnumerateArray()
            .Select(band => (band.GetProperty("key").GetString(), band.GetProperty("count").GetInt32()))
            .Should().Equal(
                ("half-or-less", 1), ("lower-25-50", 0), ("lower-10-25", 1), ("lower-under-10", 1), ("same", 10),
                ("higher-under-10", 0), ("higher-10-25", 1), ("higher-25-50", 0), ("higher-50-100", 0), ("double-or-more", 1));
    }

    [Fact(Timeout = 60_000)]
    public async Task Categories_sum_up_their_own_pairs()
    {
        var result = await GetAsync("/api/v1/insights/compare", TestContext.Current.CancellationToken);

        result.GetProperty("byCategory").EnumerateArray()
            .Select(entry => (entry.GetProperty("key").GetString(), entry.GetProperty("pairs").GetInt32(), entry.GetProperty("identical").GetInt32(),
                entry.GetProperty("lower").GetInt32(), entry.GetProperty("higher").GetInt32(), entry.GetProperty("median").GetDecimal()))
            .Should().Equal(("visits", 10, 5, 3, 2, 1m), ("surgery", 5, 5, 0, 0, 1m));
    }

    [Fact(Timeout = 60_000)]
    public async Task The_biggest_differences_come_first_either_way()
    {
        var result = await GetAsync("/api/v1/insights/compare?pageSize=5", TestContext.Current.CancellationToken);

        // 2.0 and 0.5 are equally far from 1; the larger dollar difference (A08: $130) goes first.
        Pairs(result).Should().Equal(
            (Key(8), "99213", 260m, 130m, 2.00m), (Key(5), "99213", 50m, 100m, 0.50m), (Key(4), "99213", 90m, 100m, 0.90m),
            (Key(6), "99213", 121m, 110m, 1.10m), (Key(10), "99213", 147m, 150m, 0.98m));
        result.GetProperty("pairs").GetProperty("total").GetInt32().Should().Be(15);
    }

    [Fact(Timeout = 60_000)]
    public async Task Ratios_sort_lowest_or_highest_first()
    {
        var lowest = await GetAsync("/api/v1/insights/compare?sort=ratio&pageSize=2", TestContext.Current.CancellationToken);
        var highest = await GetAsync("/api/v1/insights/compare?sort=-ratio&pageSize=2", TestContext.Current.CancellationToken);

        Pairs(lowest).Select(pair => pair.Item5).Should().Equal(0.50m, 0.90m);
        Pairs(highest).Select(pair => pair.Item5).Should().Equal(2.00m, 1.10m);
    }

    [Fact(Timeout = 60_000)]
    public async Task Facility_charge_pairs_are_left_out_unless_asked_for_and_counted_under_every_filter()
    {
        var plain = await GetAsync("/api/v1/insights/compare", TestContext.Current.CancellationToken);
        var all = await GetAsync("/api/v1/insights/compare?includeFacilityCharges=true", TestContext.Current.CancellationToken);
        var surgery = await GetAsync("/api/v1/insights/compare?category=surgery", TestContext.Current.CancellationToken);

        plain.GetProperty("facilityCharges").GetInt32().Should().Be(1);
        plain.GetProperty("summary").GetProperty("pairs").GetInt32().Should().Be(15);
        plain.GetProperty("notes").EnumerateArray().Select(note => note.GetString())
            .Should().Contain(note => note!.StartsWith("1 pair of facility charges", StringComparison.Ordinal));
        all.GetProperty("query").GetProperty("includeFacilityCharges").GetBoolean().Should().BeTrue();
        all.GetProperty("summary").GetProperty("pairs").GetInt32().Should().Be(16);
        surgery.GetProperty("facilityCharges").GetInt32().Should().Be(0, "a facility charge is never surgery");
    }

    [Fact(Timeout = 60_000)]
    public async Task Figures_need_5_providers_and_the_10_to_90_range_needs_10()
    {
        var five = (await GetAsync("/api/v1/insights/compare?setting=asc", TestContext.Current.CancellationToken)).GetProperty("summary");
        var one = await GetAsync("/api/v1/insights/compare?code=RC%200360&includeFacilityCharges=true", TestContext.Current.CancellationToken);

        (five.GetProperty("providers").GetInt32(), five.GetProperty("median").GetDecimal()).Should().Be((5, 1m));
        five.GetProperty("p10").ValueKind.Should().Be(JsonValueKind.Null);
        five.GetProperty("p90").ValueKind.Should().Be(JsonValueKind.Null);

        var summary = one.GetProperty("summary");
        summary.GetProperty("pairs").GetInt32().Should().Be(1);
        new[] { "p10", "p25", "median", "p75", "p90" }.Should().OnlyContain(name => summary.GetProperty(name).ValueKind == JsonValueKind.Null);
        var category = one.GetProperty("byCategory").EnumerateArray().Single();
        (category.GetProperty("providers").GetInt32(), category.GetProperty("median").ValueKind).Should().Be((1, JsonValueKind.Null));
    }

    [Fact(Timeout = 60_000)]
    public async Task A_pair_says_same_only_when_the_two_rates_are_equal()
    {
        var result = await GetAsync("/api/v1/insights/compare?pageSize=100", TestContext.Current.CancellationToken);

        result.GetProperty("pairs").GetProperty("items").EnumerateArray()
            .Should().OnlyContain(pair => pair.GetProperty("same").GetBoolean() == (pair.GetProperty("productValue").GetDecimal() == pair.GetProperty("versusValue").GetDecimal()));
        result.GetProperty("pairs").GetProperty("items").EnumerateArray().Count(pair => pair.GetProperty("same").GetBoolean())
            .Should().Be(result.GetProperty("summary").GetProperty("identical").GetInt32());
    }

    [Fact(Timeout = 60_000)]
    public async Task Pairs_priced_in_different_units_are_left_out_and_the_notes_say_so()
    {
        var result = await GetAsync("/api/v1/insights/compare?code=J0585", TestContext.Current.CancellationToken);

        result.GetProperty("summary").GetProperty("pairs").GetInt32().Should().Be(0);
        result.GetProperty("notes").EnumerateArray().Select(note => note.GetString())
            .Should().Equal("1 pair left out: the two products price it in different units.");
    }

    [Theory(Timeout = 60_000)]
    [InlineData("code=99213", 10)]
    [InlineData("code=CPT%2045378", 5)]
    [InlineData("category=surgery", 5)]
    [InlineData("setting=asc", 5)]
    [InlineData("setting=office&category=visits", 10)]
    [InlineData("setting=er", 0)]
    public async Task Filters_narrow_the_pairs(string filter, int pairs)
    {
        var result = await GetAsync($"/api/v1/insights/compare?{filter}", TestContext.Current.CancellationToken);

        result.GetProperty("summary").GetProperty("pairs").GetInt32().Should().Be(pairs);
        result.GetProperty("pairs").GetProperty("total").GetInt32().Should().Be(pairs);
    }

    [Fact(Timeout = 60_000)]
    public async Task Swapping_the_products_turns_each_ratio_around()
    {
        var swapped = await GetAsync($"/api/v1/insights/compare?product={Ambetter}&versus={Essential}&sort=ratio&pageSize=1", TestContext.Current.CancellationToken);

        swapped.GetProperty("product").GetProperty("key").GetString().Should().Be(Ambetter);
        Pairs(swapped).Single().Should().Be((Key(8), "99213", 130m, 260m, 0.50m));
    }

    [Fact(Timeout = 60_000)]
    public async Task Each_pair_links_to_the_two_searches_that_show_its_rates()
    {
        using var client = factory.CreateClient();
        var pair = (await GetAsync("/api/v1/insights/compare?pageSize=1", TestContext.Current.CancellationToken)).GetProperty("pairs").GetProperty("items")[0];

        foreach (var (link, value) in new[] { ("productSearch", "productValue"), ("versusSearch", "versusValue") })
        {
            var query = pair.GetProperty(link).Deserialize<Dictionary<string, string>>()!;
            var search = await client.GetFromJsonAsync<JsonElement>(
                $"/api/v1/rates?{string.Join('&', query.Select(entry => $"{entry.Key}={Uri.EscapeDataString(entry.Value)}"))}",
                TestContext.Current.CancellationToken);
            search.GetProperty("rows").GetProperty("items").EnumerateArray().Single().GetProperty("rate").GetDecimal()
                .Should().Be(pair.GetProperty(value).GetDecimal());
        }
    }

    [Theory(Timeout = 60_000)]
    [InlineData("product=essential-plan&versus=essential-plan", "versus")]
    [InlineData("product=dental", "product")]
    [InlineData("versus=dental", "versus")]
    [InlineData("sort=savings", "sort")]
    [InlineData("category=%00", "category")]
    public async Task Bad_choices_get_a_message_under_their_own_name(string filter, string field)
    {
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(TestContext.Current.CancellationToken);

        using var response = await client.GetAsync($"/api/v1/insights/compare?{filter}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    private async Task<JsonElement> GetAsync(string url, CancellationToken ct)
    {
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);
        return await client.GetFromJsonAsync<JsonElement>(url, ct);
    }

    private static List<(string, string, decimal, decimal, decimal)> Pairs(JsonElement result) =>
        result.GetProperty("pairs").GetProperty("items").EnumerateArray()
            .Select(pair => (
                pair.GetProperty("providerKey").GetString()!,
                pair.GetProperty("code").GetString()!,
                pair.GetProperty("productValue").GetDecimal(),
                pair.GetProperty("versusValue").GetDecimal(),
                pair.GetProperty("ratio").GetDecimal()))
            .ToList();
}
