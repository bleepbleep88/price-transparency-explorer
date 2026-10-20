using System.Net.Http.Json;
using System.Text.Json;
using PriceExplorer.Api.Core.Queries.Statistics;
using PriceExplorer.Tests.Support;

namespace PriceExplorer.Tests.Insights;

/// <summary>
/// The insights on the real Fidelis slices agree with search: every provider's scorecard, number for number, with a
/// search for its EIN; every product pair with the two searches it links to.
/// </summary>
public sealed class InsightsOnFixtureTests(ExplorerApiFactory factory) : IClassFixture<ExplorerApiFactory>
{
    [Fact(Timeout = 120_000)]
    public async Task Every_providers_scorecard_matches_the_search_for_its_ein_group_by_group()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);
        var saved = await factory.SendAsync(new GetGroupStatsQuery(), ct);
        var providers = saved.Groups.SelectMany(group => group.Providers).Select(value => value.ProviderKey).Distinct().Order(StringComparer.Ordinal).ToList();

        var checkedGroups = 0;
        foreach (var provider in providers)
        {
            var scorecard = await client.GetFromJsonAsync<JsonElement>($"/api/v1/insights/providers/{provider}", ct);
            scorecard.GetProperty("groups").GetArrayLength()
                .Should().Be(saved.Groups.Count(group => group.Providers.Any(value => value.ProviderKey == provider)), provider);

            // One search per code with the provider's EIN shows its row in every group of that code.
            foreach (var code in scorecard.GetProperty("groups").EnumerateArray().GroupBy(group => (Type: Text(group, "codeType"), Code: Text(group, "code"))))
            {
                var search = await client.GetFromJsonAsync<JsonElement>(
                    $"/api/v1/rates?code={code.Key.Code}&codeType={code.Key.Type}&ein={Uri.EscapeDataString(provider)}&pageSize=100", ct);
                var rows = search.GetProperty("rows").GetProperty("items").EnumerateArray().ToDictionary(row => Text(row, "groupKey"));
                foreach (var group in code)
                {
                    var row = rows[Text(group, "groupKey")];
                    (row.GetProperty("rate").GetDecimal(), Number(row, "percentile"), Number(row, "vsMedian"), row.GetProperty("band").GetString())
                        .Should().Be((group.GetProperty("value").GetDecimal(), Number(group, "percentile"), Number(group, "vsMedian"),
                            group.GetProperty("band").GetString()), $"{provider} in {Text(group, "groupKey")}");
                    checkedGroups++;
                }
            }
        }

        providers.Should().HaveCountGreaterThan(20);
        checkedGroups.Should().BeGreaterThan(providers.Count);
    }

    [Fact(Timeout = 60_000)]
    public async Task Every_product_pair_shows_the_rates_its_two_searches_show()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);

        var comparison = await client.GetFromJsonAsync<JsonElement>("/api/v1/insights/compare?pageSize=100", ct);
        var pairs = comparison.GetProperty("pairs").GetProperty("items").EnumerateArray().ToList();

        pairs.Should().NotBeEmpty("45378 and 29881 have providers in both products");
        foreach (var pair in pairs)
        {
            foreach (var (link, value) in new[] { ("productSearch", "productValue"), ("versusSearch", "versusValue") })
            {
                var query = pair.GetProperty(link).Deserialize<Dictionary<string, string>>()!;
                var search = await client.GetFromJsonAsync<JsonElement>(
                    $"/api/v1/rates?{string.Join('&', query.Select(entry => $"{entry.Key}={Uri.EscapeDataString(entry.Value)}"))}", ct);
                search.GetProperty("rows").GetProperty("items").EnumerateArray().Single().GetProperty("rate").GetDecimal()
                    .Should().Be(pair.GetProperty(value).GetDecimal());
            }
        }
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString()!;

    private static decimal? Number(JsonElement parent, string name) =>
        parent.GetProperty(name) is { ValueKind: JsonValueKind.Number } number ? number.GetDecimal() : null;
}
