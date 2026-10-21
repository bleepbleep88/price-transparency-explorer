using System.Net.Http.Json;
using System.Text.Json;
using PriceExplorer.Tests.Support;
using static PriceExplorer.Tests.Support.TicFileBuilder;

namespace PriceExplorer.Tests.Api;

/// <summary>
/// A network's name on many practices' tax IDs: each practice shows its own name wherever names are shown (search rows,
/// the name filter, the source drawer, the scorecard, the typeahead), and the plan's name stays traceable. The Essential
/// Plan file is swapped for a small synthetic one: "Big Network IPA" on six tax IDs (its home, 55-0000000, on two
/// groups), one of which (55-0000001) the file also lists as "Lin Family Medicine PC", and "Four Doctors Group" on four.
/// </summary>
public sealed class PracticeNameTests(PracticeNameTests.Factory factory) : IClassFixture<PracticeNameTests.Factory>
{
    private const string Lin = "550000001";

    /// <summary>The real API with the synthetic Essential Plan file.</summary>
    public sealed class Factory() : ExplorerApiFactory(http => http.ServeJson(FixtureHttpHandler.EssentialUrl, SyntheticFile), null, null);

    private static string Group(int id, string ein, string name, long npi) => $$"""
        { "provider_group_id": {{id}}, "network_name": [], "provider_groups": [
          { "npi": [{{npi}}], "tin": { "type": "ein", "value": "{{ein}}", "business_name": "{{name}}" } } ] }
        """;

    private static readonly string[] Groups =
    [
        Group(11, "55-0000000", "Big Network IPA", 1000000011),
        Group(12, "55-0000000", "Big Network IPA", 1000000012),
        .. Enumerable.Range(1, 5).Select(i => Group(12 + i, $"55-000000{i}", "Big Network IPA", 1000000012 + i)),
        Group(18, "55-0000001", "Lin Family Medicine PC", 1000000013),
        .. Enumerable.Range(6, 4).Select(i => Group(13 + i, $"66-000000{i}", "Four Doctors Group", 1000000013 + i)),
    ];

    /// <summary>An office visit per group, each at its own price; the Lin name is only on a 99214 rate.</summary>
    private static string SyntheticFile => File(
        Groups,
        [
            .. new[] { 11, 12, 13, 14, 15, 16, 17, 19, 20, 21, 22 }.Select(id =>
                Item("CPT", "99213", "Office visit", $"[{id}]", Price(rate: $"{80 + id}.00", serviceCode: "[\"11\"]", billingClass: "professional"))),
            Item("CPT", "99214", "Office visit", "[18]", Price(rate: "140.00", serviceCode: "[\"11\"]", billingClass: "professional")),
        ]);

    [Fact(Timeout = 120_000)]
    public async Task Each_practice_under_a_network_name_shows_its_own_name_and_keeps_the_plans()
    {
        var rows = await RowsAsync("/api/v1/rates?code=99213&product=essential-plan", TestContext.Current.CancellationToken);

        rows.Select(row => (row.GetProperty("providerKey").GetString(), row.GetProperty("businessName").GetString(), ListedAs(row)))
            .OrderBy(row => row.Item1, StringComparer.Ordinal)
            .Should().Equal(
                ("550000000", "Big Network IPA", null),
                (Lin, "Lin Family Medicine PC", "Big Network IPA"),
                ("550000002", "Big Network IPA", null),
                ("550000003", "Big Network IPA", null),
                ("550000004", "Big Network IPA", null),
                ("550000005", "Big Network IPA", null),
                ("660000006", "Four Doctors Group", null),
                ("660000007", "Four Doctors Group", null),
                ("660000008", "Four Doctors Group", null),
                ("660000009", "Four Doctors Group", null));
    }

    [Fact(Timeout = 120_000)]
    public async Task The_name_filter_finds_a_practice_by_its_own_name_or_the_plans()
    {
        (await RowsAsync("/api/v1/rates?code=99213&product=essential-plan&provider=lin%20family", TestContext.Current.CancellationToken))
            .Select(row => row.GetProperty("providerKey").GetString()).Should().Equal(Lin);
        (await RowsAsync("/api/v1/rates?code=99213&product=essential-plan&provider=big%20network", TestContext.Current.CancellationToken))
            .Select(row => row.GetProperty("providerKey").GetString()).Should().Contain([Lin, "550000000"]);
    }

    [Fact(Timeout = 120_000)]
    public async Task The_source_lists_the_group_as_published_with_the_name_shown()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();
        var row = (await RowsAsync("/api/v1/rates?code=99213&product=essential-plan", TestContext.Current.CancellationToken)).Single(item => item.GetProperty("providerKey").GetString() == Lin);

        var source = await client.GetFromJsonAsync<JsonElement>($"/api/v1/rates/{row.GetProperty("firstRateId").GetInt64()}/source?provider={Lin}", ct);
        var group = source.GetProperty("providerGroups")[0];
        group.GetProperty("businessName").GetString().Should().Be("Big Network IPA");
        group.GetProperty("shownName").GetString().Should().Be("Lin Family Medicine PC");
    }

    [Fact(Timeout = 120_000)]
    public async Task The_scorecard_and_the_typeahead_use_the_practices_own_name()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);

        var scorecard = await client.GetFromJsonAsync<JsonElement>($"/api/v1/insights/scorecard?provider={Lin}", ct);
        scorecard.GetProperty("provider").GetProperty("name").GetString().Should().Be("Lin Family Medicine PC");
        scorecard.GetProperty("provider").GetProperty("otherNames").EnumerateArray().Should().BeEmpty();

        var suggestions = await client.GetFromJsonAsync<JsonElement>("/api/v1/providers?q=lin%20family", ct);
        suggestions.EnumerateArray().Select(item => item.GetProperty("businessName").GetString()).Should().Equal("Lin Family Medicine PC");
    }

    private async Task<IReadOnlyList<JsonElement>> RowsAsync(string url, CancellationToken ct)
    {
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);
        var result = await client.GetFromJsonAsync<JsonElement>(url, ct);
        return result.GetProperty("rows").GetProperty("items").EnumerateArray().ToList();
    }

    private static string? ListedAs(JsonElement row) =>
        row.GetProperty("listedAs").ValueKind == JsonValueKind.Null ? null : row.GetProperty("listedAs").GetString();
}
