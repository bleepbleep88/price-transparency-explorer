using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static PriceExplorer.Tests.Insights.InsightFiles;

namespace PriceExplorer.Tests.Insights;

/// <summary>GET /api/v1/insights/scorecard?provider={provider} on the synthetic files (see <see cref="InsightFiles"/>).</summary>
public sealed class ProviderScorecardTests(InsightsApiFactory factory) : IClassFixture<InsightsApiFactory>
{
    [Fact(Timeout = 60_000)]
    public async Task Each_group_shows_the_providers_rate_percentile_and_times_median()
    {
        var scorecard = await GetAsync($"/api/v1/insights/scorecard?provider={Key(3)}", TestContext.Current.CancellationToken);

        // A03: Ambetter 99213 at 100 (2 below, 2 others at 100 among 11 others), 45378 at 500, J0585 at 3;
        // Essential Plan 99213 at 100 (the median; 4 below, M also at 100) and 45378 at 500 (the median of 5).
        Groups(scorecard).Should().Equal(
            ("CPT", "45378", Ambetter, 500m, 22, 0.80m, "low"),
            ("CPT", "45378", Essential, 500m, 50, 1.00m, "typical"),
            ("CPT", "99213", Ambetter, 100m, 27, 0.87m, "low"),
            ("CPT", "99213", Essential, 100m, 45, 1.00m, "typical"),
            ("HCPCS", "J0585", Ambetter, 3m, 22, 0.55m, "low"));
    }

    [Fact(Timeout = 60_000)]
    public async Task The_price_index_is_the_median_times_median_and_the_lowest_groups_come_first()
    {
        var scorecard = await GetAsync($"/api/v1/insights/scorecard?provider={Key(3)}", TestContext.Current.CancellationToken);

        var index = scorecard.GetProperty("index");
        index.GetProperty("value").GetDecimal().Should().Be(0.87m, "the median of 0.55, 0.80, 0.87, 1.00 and 1.00");
        (index.GetProperty("groupsCounted").GetInt32(), index.GetProperty("below").GetInt32(), index.GetProperty("atMedian").GetInt32(),
                index.GetProperty("above").GetInt32())
            .Should().Be((5, 3, 2, 0));
        scorecard.GetProperty("highest").EnumerateArray().Should().BeEmpty("none of its rates is above the median");
        scorecard.GetProperty("lowest").EnumerateArray().Select(group => group.GetProperty("vsMedian").GetDecimal())
            .Should().Equal(0.55m, 0.80m, 0.87m);
    }

    [Fact(Timeout = 60_000)]
    public async Task Fewer_than_five_groups_with_a_median_give_no_index_and_the_highest_list_their_furthest_first()
    {
        var scorecard = await GetAsync($"/api/v1/insights/scorecard?provider={Key(8)}", TestContext.Current.CancellationToken);

        var index = scorecard.GetProperty("index");
        index.GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);
        index.GetProperty("groupsCounted").GetInt32().Should().Be(4);
        index.GetProperty("minimumGroups").GetInt32().Should().Be(5);
        scorecard.GetProperty("highest").EnumerateArray().Select(group => group.GetProperty("vsMedian").GetDecimal())
            .Should().Equal(2.60m, 1.45m, 1.28m, 1.13m);
    }

    [Fact(Timeout = 60_000)]
    public async Task The_provider_is_named_with_its_npis_products_and_whole_tax_id_contracts()
    {
        var a03 = (await GetAsync($"/api/v1/insights/scorecard?provider={Key(3)}", TestContext.Current.CancellationToken)).GetProperty("provider");
        var a02 = (await GetAsync($"/api/v1/insights/scorecard?provider=21-0000002", TestContext.Current.CancellationToken)).GetProperty("provider");

        a03.GetProperty("name").GetString().Should().Be("Clinic A03");
        a03.GetProperty("einDisplay").GetString().Should().Be("21-0000003");
        a03.GetProperty("npiCount").GetInt32().Should().Be(3, "two NPIs in each product, one of them in both");
        a03.GetProperty("wholeTaxIdContract").GetBoolean().Should().BeFalse();
        a03.GetProperty("products").EnumerateArray().Select(p => (p.GetProperty("key").GetString(), p.GetProperty("groups").GetInt32()))
            .Should().Equal((Ambetter, 3), (Essential, 2));
        a02.GetProperty("providerKey").GetString().Should().Be(Key(2), "an EIN with a dash finds its key");
        a02.GetProperty("wholeTaxIdContract").GetBoolean().Should().BeTrue("its Ambetter contract lists npi [0]");
        a02.GetProperty("npiCount").GetInt32().Should().Be(2);
    }

    [Fact(Timeout = 60_000)]
    public async Task A_masked_tax_id_is_found_by_its_full_number_but_never_shown()
    {
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(TestContext.Current.CancellationToken);
        using var response = await client.GetAsync($"/api/v1/insights/scorecard?provider={MaskedEin}", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().NotContain(MaskedEin).And.NotContain("07-1234567");
        var provider = JsonDocument.Parse(body).RootElement.GetProperty("provider");
        provider.GetProperty("providerKey").GetString().Should().MatchRegex("^u[0-9a-f]{12}$");
        provider.GetProperty("einDisplay").GetString().Should().Be("••-•••4567");
        provider.GetProperty("ein").ValueKind.Should().Be(JsonValueKind.Null);
        provider.GetProperty("einMasked").GetBoolean().Should().BeTrue();

        var byKey = await GetAsync($"/api/v1/insights/scorecard?provider={provider.GetProperty("providerKey").GetString()}", TestContext.Current.CancellationToken);
        byKey.GetProperty("groups").GetArrayLength().Should().Be(1);
    }

    [Fact(Timeout = 60_000)]
    public async Task A_tax_id_that_isnt_an_ein_is_found_by_its_key()
    {
        var scorecard = await GetAsync($"/api/v1/insights/scorecard?provider={TypedKey}", TestContext.Current.CancellationToken);

        scorecard.GetProperty("provider").GetProperty("einDisplay").GetString().Should().Be("NPI 1234567893");
        Groups(scorecard).Should().ContainSingle().Which.Should().Be(("CPT", "99214", Ambetter, 170m, null, null, null));
    }

    [Fact(Timeout = 60_000)]
    public async Task Every_group_reads_exactly_as_the_search_its_link_opens()
    {
        using var client = factory.CreateClient();
        foreach (var provider in Enumerable.Range(1, 12).Select(Key).Append(TypedKey))
        {
            var scorecard = await GetAsync($"/api/v1/insights/scorecard?provider={Uri.EscapeDataString(provider)}", TestContext.Current.CancellationToken);
            foreach (var group in scorecard.GetProperty("groups").EnumerateArray())
            {
                var link = group.GetProperty("search").Deserialize<Dictionary<string, string>>()!;
                link["ein"].Should().Be(provider);
                var search = await client.GetFromJsonAsync<JsonElement>(
                    $"/api/v1/rates?{string.Join('&', link.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"))}",
                    TestContext.Current.CancellationToken);

                var row = search.GetProperty("rows").GetProperty("items").EnumerateArray().Single();
                row.GetProperty("groupKey").GetString().Should().Be(group.GetProperty("groupKey").GetString());
                row.GetProperty("rate").GetDecimal().Should().Be(group.GetProperty("value").GetDecimal());
                Number(row, "percentile").Should().Be(Number(group, "percentile"));
                Number(row, "vsMedian").Should().Be(Number(group, "vsMedian"));
                row.GetProperty("band").GetString().Should().Be(group.GetProperty("band").GetString());
            }
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task The_provider_is_required()
    {
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(TestContext.Current.CancellationToken);

        using var response = await client.GetAsync("/api/v1/insights/scorecard", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("errors").TryGetProperty("provider", out _).Should().BeTrue();
    }

    [Theory(Timeout = 60_000)]
    [InlineData("not-a-key", HttpStatusCode.BadRequest, "validation_failed")]
    [InlineData("12-345", HttpStatusCode.BadRequest, "validation_failed")]
    [InlineData("99-9999999", HttpStatusCode.NotFound, "provider_not_found")]
    public async Task An_unknown_or_malformed_provider_says_so(string provider, HttpStatusCode status, string code)
    {
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(TestContext.Current.CancellationToken);

        using var response = await client.GetAsync($"/api/v1/insights/scorecard?provider={Uri.EscapeDataString(provider)}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(status);
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("code").GetString().Should().Be(code);
    }

    private async Task<JsonElement> GetAsync(string url, CancellationToken ct)
    {
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);
        return await client.GetFromJsonAsync<JsonElement>(url, ct);
    }

    private static decimal? Number(JsonElement parent, string name) =>
        parent.GetProperty(name) is { ValueKind: JsonValueKind.Number } number ? number.GetDecimal() : null;

    private static List<(string, string, string, decimal, int?, decimal?, string?)> Groups(JsonElement scorecard) =>
        scorecard.GetProperty("groups").EnumerateArray()
            .Select(group => (
                group.GetProperty("codeType").GetString()!,
                group.GetProperty("code").GetString()!,
                group.GetProperty("productKey").GetString()!,
                group.GetProperty("value").GetDecimal(),
                group.GetProperty("percentile") is { ValueKind: JsonValueKind.Number } percentile ? percentile.GetInt32() : (int?)null,
                group.GetProperty("vsMedian") is { ValueKind: JsonValueKind.Number } vsMedian ? vsMedian.GetDecimal() : (decimal?)null,
                group.GetProperty("band").GetString()))
            .ToList();
}
