using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PriceExplorer.Tests.Support;

namespace PriceExplorer.Tests.Api;

/// <summary>End-to-end search over the real Fidelis slices (see Fixtures/README.md). Expected numbers were computed independently.</summary>
public sealed class SearchApiTests(ExplorerApiFactory factory) : IClassFixture<ExplorerApiFactory>
{
    [Fact(Timeout = 60_000)]
    public async Task A_code_published_only_as_facility_rows_is_found_through_its_revenue_code()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken, new() { ["code"] = "29881" });

        result.GetProperty("code").GetProperty("foundVia").EnumerateArray().Select(v => v.GetString())
            .Should().Equal("Facility rates under revenue code 0490 (Ambulatory surgery)");

        var group = Groups(result).Should().ContainSingle().Subject;
        group.GetProperty("feeType").GetString().Should().Be("facility");
        group.GetProperty("settingKey").GetString().Should().Be("asc");
        group.GetProperty("statsLevel").GetString().Should().Be("list");
        group.GetProperty("listedRates").EnumerateArray().Select(r => r.GetDecimal()).Should().Equal(613.33m, 1183.01m);
        group.GetProperty("billedAs")[0].GetProperty("label").GetString().Should().Be("Revenue code 0490 (Ambulatory surgery)");

        Hints(result).Should().Contain("code_facility_only");
        result.GetProperty("quality").GetProperty("duplicatesCollapsed").GetInt64().Should().Be(2);
    }

    [Fact(Timeout = 60_000)]
    public async Task Rates_are_split_into_fair_comparison_groups()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken, new() { ["code"] = "45378" });

        var groups = Groups(result).Select(g => (
            Product: g.GetProperty("productKey").GetString(),
            Fee: g.GetProperty("feeType").GetString(),
            Setting: g.GetProperty("settingKey").GetString(),
            Modifiers: string.Join(",", g.GetProperty("modifiers").EnumerateArray().Select(m => m.GetProperty("code").GetString())),
            Providers: g.GetProperty("providerCount").GetInt32())).ToList();

        groups.Should().Contain(("ambetter-from-fidelis-care", "facility", "asc", "", 2));
        groups.Should().Contain(("ambetter-from-fidelis-care", "professional", "asc", "33", 5));
        groups.Should().Contain(("ambetter-from-fidelis-care", "professional", "hospital-outpatient", "33", 3));
        groups.Should().Contain(("ambetter-from-fidelis-care", "professional", "office", "SU", 3));
        groups.Should().Contain(("essential-plan", "professional", "asc", "33", 49));

        var essentialAsc = Groups(result).Single(g => g.GetProperty("productKey").GetString() == "essential-plan"
            && g.GetProperty("feeType").GetString() == "professional" && g.GetProperty("settingKey").GetString() == "asc");
        essentialAsc.GetProperty("statsLevel").GetString().Should().Be("full");
        essentialAsc.GetProperty("notes").EnumerateArray().Select(n => n.GetString())
            .Should().Contain(n => n!.Contains("paid exactly $167.09"));
    }

    [Fact(Timeout = 60_000)]
    public async Task An_npi_narrows_rows_and_highlights_where_the_provider_sits_while_stats_stay_market_wide()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken, new() { ["code"] = "45378", ["npi"] = "1174873780" });

        var rows = result.GetProperty("rows").GetProperty("items").EnumerateArray().ToList();
        rows.Should().NotBeEmpty().And.OnlyContain(r => r.GetProperty("ein").GetString() == "460857425");
        var group = Groups(result).Should().ContainSingle().Subject;
        group.GetProperty("highlights").EnumerateArray().Should().ContainSingle();
        group.GetProperty("providerCount").GetInt32().Should().BeGreaterThan(1, "stats still describe every provider in the group");
    }

    [Fact(Timeout = 60_000)]
    public async Task An_npi_under_a_different_ein_explains_the_mismatch()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken, new() { ["code"] = "45378", ["npi"] = "1174873780", ["ein"] = "11-3471596" });

        Groups(result).Should().BeEmpty();
        Hints(result).Should().Contain("npi_other_ein");
    }

    [Fact(Timeout = 60_000)]
    public async Task A_code_with_no_rates_suggests_codes_that_start_the_same_way()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken, new() { ["code"] = "45379" });

        var hint = result.GetProperty("hints").EnumerateArray().Single(h => h.GetProperty("kind").GetString() == "code_not_found");
        hint.GetProperty("links").EnumerateArray().Select(l => l.GetProperty("query").GetProperty("code").GetString())
            .Should().Contain("45378");
    }

    [Fact(Timeout = 60_000)]
    public async Task Short_codes_get_their_leading_zeros_back()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken, new() { ["code"] = "812" });

        var code = result.GetProperty("code");
        code.GetProperty("code").GetString().Should().Be("00812");
        code.GetProperty("category").GetProperty("unit").GetString().Should().Be("per anesthesia unit");
    }

    [Fact(Timeout = 60_000)]
    public async Task Missing_or_malformed_inputs_return_camel_case_field_errors()
    {
        using var client = await ReadyClientAsync(TestContext.Current.CancellationToken);

        using var missing = await client.GetAsync("/api/v1/rates", TestContext.Current.CancellationToken);
        using var badNpi = await client.GetAsync("/api/v1/rates?code=45378&npi=123", TestContext.Current.CancellationToken);
        using var badPage = await client.GetAsync("/api/v1/rates?code=45378&page=abc", TestContext.Current.CancellationToken);

        (await Errors(missing)).Should().ContainKey("code");
        (await Errors(badNpi)).Should().ContainKey("npi");
        (await Errors(badPage)).Should().ContainKey("page");
    }

    [Fact(Timeout = 60_000)]
    public async Task Every_rate_traces_back_to_its_spot_in_the_source_file()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken, new() { ["code"] = "29881" });
        var rateId = result.GetProperty("rows").GetProperty("items")[0].GetProperty("firstRateId").GetInt64();
        using var client = await ReadyClientAsync(TestContext.Current.CancellationToken);

        var source = await client.GetFromJsonAsync<JsonElement>($"/api/v1/rates/{rateId}/source", TestContext.Current.CancellationToken);

        source.GetProperty("jsonPath").GetString().Should().StartWith("$.in_network[");
        source.GetProperty("billingCodePublished").GetString().Should().Be("490");
        source.GetProperty("billingCode").GetString().Should().Be("0490");
        source.GetProperty("codeSource").GetString().Should().Be("additional_information");
        source.GetProperty("occurrences").GetInt32().Should().Be(2);
        source.GetProperty("itemLabelCount").GetInt32().Should().Be(2);
        source.GetProperty("file").GetProperty("url").GetString().Should().Be(FixtureHttpHandler.AmbetterUrl);
        source.GetProperty("snapshot").GetProperty("indexUrl").GetString().Should().Be(FixtureHttpHandler.IndexUrl);
    }

    [Fact(Timeout = 60_000)]
    public async Task Typeahead_finds_codes_by_name_and_by_number_without_leading_zeros()
    {
        using var client = await ReadyClientAsync(TestContext.Current.CancellationToken);

        var byName = await client.GetFromJsonAsync<JsonElement>("/api/v1/codes?q=colonoscopy", TestContext.Current.CancellationToken);
        var byNumber = await client.GetFromJsonAsync<JsonElement>("/api/v1/codes?q=812", TestContext.Current.CancellationToken);

        byName.EnumerateArray().Select(c => c.GetProperty("code").GetString()).Should().Contain("45378");
        byNumber[0].GetProperty("code").GetString().Should().Be("00812");
    }

    private async Task<HttpClient> ReadyClientAsync(CancellationToken cancellationToken)
    {
        var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(cancellationToken);
        return client;
    }

    private async Task<JsonElement> SearchAsync(CancellationToken cancellationToken, Dictionary<string, string> query)
    {
        using var client = await ReadyClientAsync(cancellationToken);
        var url = "/api/v1/rates?" + string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        using var response = await client.GetAsync(url, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    private static List<JsonElement> Groups(JsonElement result) => result.GetProperty("groups").EnumerateArray().ToList();

    private static List<string?> Hints(JsonElement result) =>
        result.GetProperty("hints").EnumerateArray().Select(h => h.GetProperty("kind").GetString()).ToList();

    private static async Task<Dictionary<string, string[]>> Errors(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        problem.GetProperty("code").GetString().Should().Be("validation_failed");
        return problem.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
    }
}
