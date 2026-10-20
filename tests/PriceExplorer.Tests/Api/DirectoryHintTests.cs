using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PriceExplorer.Tests.Support;
using Serilog.Events;

namespace PriceExplorer.Tests.Api;

/// <summary>
/// Hints for the specialty and location filters, over the real fixture slices and the bundled provider directory.
/// Yorkville Endoscopy (NPI 1174873780) is a Manhattan surgery center with one CPT 45378 facility rate; CPT 29881's
/// rates belong to surgery centers in Brooklyn and Albany County.
/// </summary>
public sealed class DirectoryHintTests(LogCaptureApiFactory factory) : IClassFixture<LogCaptureApiFactory>
{
    private const string YorkvilleNpi = "1174873780";

    /// <summary>Searches whose hints carry links, across every hint the directory filters can produce.</summary>
    public static TheoryData<string> SearchesWithLinks =>
    [
        "code=29881&county=Tompkins",
        "code=29881&specialty=Podiatr",
        "code=29881&county=Tompkins&specialty=Podiatr",
        "code=45378&county=Manhatan",
        "code=45378&specialty=zzzz",
        $"code=45378&npi={YorkvilleNpi}&county=Queens",
        "code=45378&provider=yorkville&specialty=gastro",
        "code=45378&specialty=gastro&county=Oneida",
        $"code=45378&npi={YorkvilleNpi}&provider=mercy&county=Queens",
        $"code=45378&npi={YorkvilleNpi}&ein=113471596&county=Manhattan",
        "code=45378&county=Albany&feeType=facility",
        "code=29881&codeType=HCPCS&county=Brooklyn",
        "code=29881&codeType=HCPCS&county=Queens",
        "code=4537&county=Manhattan",
        "code=45399&county=Queens",
        "code=0490&county=Manhattan",
        "code=0490&specialty=gastro",
        $"code=0490&npi={YorkvilleNpi}&specialty=gastro",
        $"code=0490&npi={YorkvilleNpi}&county=Queens",
        "code=0490&provider=yorkville&county=Queens",
        "code=812&specialty=gastro",
    ];

    [Theory(Timeout = 120_000)]
    [MemberData(nameof(SearchesWithLinks))]
    public async Task Every_hint_link_finds_rates_and_none_had_to_be_dropped(string search)
    {
        var ct = TestContext.Current.CancellationToken;
        var query = Parse(search);
        var result = await SearchAsync(ct, query);
        var links = result.GetProperty("hints").EnumerateArray().SelectMany(hint => hint.GetProperty("links").EnumerateArray()).ToList();

        links.Should().NotBeEmpty($"{search} should offer a way forward");
        foreach (var link in links)
        {
            var followed = new Dictionary<string, string>(query);
            foreach (var value in link.GetProperty("query").EnumerateObject())
            {
                if (value.Value.ValueKind == JsonValueKind.Null)
                {
                    followed.Remove(value.Name);
                }
                else
                {
                    followed[value.Name] = value.Value.GetString()!;
                }
            }

            var next = await SearchAsync(ct, followed);
            next.GetProperty("rows").GetProperty("total").GetInt32().Should().BePositive(
                $"'{link.GetProperty("label").GetString()}' from {search} leads to {string.Join("&", followed.Select(kv => $"{kv.Key}={kv.Value}"))}");
        }

        // The link checker drops a link that finds nothing and logs it: a hint built wrong.
        factory.Events.Where(e => e.Level >= LogEventLevel.Warning).Select(e => e.RenderMessage())
            .Should().NotContain(message => message.StartsWith("Dropped a", StringComparison.Ordinal));
    }

    [Fact(Timeout = 60_000)]
    public async Task A_place_without_rates_for_the_code_is_named_as_it_matched()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken, new() { ["code"] = "29881", ["county"] = "Tompkins" });

        var hint = Hint(result, "county_no_match");
        hint.GetProperty("message").GetString().Should().Be("No provider in Tompkins County has a rate for CPT 29881.");
        Links(hint).Should().Equal(("Clear the location filter", """{"county":null}"""));
    }

    [Fact(Timeout = 60_000)]
    public async Task A_place_or_specialty_that_matches_no_one_says_only_that()
    {
        var ct = TestContext.Current.CancellationToken;
        var place = await SearchAsync(ct, new() { ["code"] = "45378", ["county"] = "Manhatan" });
        var specialty = await SearchAsync(ct, new() { ["code"] = "45378", ["specialty"] = "zzzz" });
        var noRate = await SearchAsync(ct, new() { ["code"] = "29881", ["specialty"] = "Podiatr" });

        Hint(place, "county_no_match").GetProperty("message").GetString().Should().Be("No providers here matched \"Manhatan\".");
        Hint(specialty, "specialty_no_match").GetProperty("message").GetString().Should().Be("No providers here matched \"zzzz\".");
        Hint(noRate, "specialty_no_match").GetProperty("message").GetString().Should().Be(
            "No provider with a specialty matching \"Podiatr\" has a rate for CPT 29881.");
    }

    [Fact(Timeout = 60_000)]
    public async Task When_both_filters_fail_each_link_clears_both()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken,
            new() { ["code"] = "29881", ["county"] = "Tompkins", ["specialty"] = "Podiatr" });

        Links(Hint(result, "specialty_no_match")).Should().Equal(("Clear the specialty and location filters", """{"specialty":null,"county":null}"""));
        Links(Hint(result, "county_no_match")).Should().Equal(("Clear the specialty and location filters", """{"specialty":null,"county":null}"""));
    }

    [Fact(Timeout = 60_000)]
    public async Task An_npi_with_rates_elsewhere_is_told_the_location_is_the_reason()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken,
            new() { ["code"] = "45378", ["npi"] = YorkvilleNpi, ["county"] = "Queens" });

        var hint = Hint(result, "provider_filters_conflict");
        hint.GetProperty("message").GetString().Should().Be("NPI 1174873780 has rates for CPT 45378, but none in Queens.");
        Links(hint).Should().Equal(("Clear the location filter", """{"county":null}"""));
    }

    [Fact(Timeout = 60_000)]
    public async Task A_name_and_a_specialty_that_never_meet_are_named_together()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken,
            new() { ["code"] = "45378", ["provider"] = "yorkville", ["specialty"] = "gastro" });

        var hint = Hint(result, "provider_filters_conflict");
        hint.GetProperty("message").GetString().Should().Be(
            "Providers whose name contains \"yorkville\" have rates for CPT 45378, but none with a specialty matching \"gastro\".");
        Links(hint).Should().Equal(("Clear the specialty filter", """{"specialty":null}"""));
    }

    [Fact(Timeout = 60_000)]
    public async Task A_specialty_and_a_place_that_each_have_rates_but_never_on_one_npi_say_so()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken,
            new() { ["code"] = "45378", ["specialty"] = "gastro", ["county"] = "Oneida" });

        var hint = Hint(result, "provider_filters_conflict");
        hint.GetProperty("message").GetString().Should().Be(
            "No CPT 45378 rate lists an NPI with a specialty matching \"gastro\" in Oneida County. Each filter finds rates on its own.");
        Links(hint).Select(link => link.Label).Should().Equal("Clear the specialty filter", "Clear the location filter");
    }

    [Fact(Timeout = 60_000)]
    public async Task Clearing_the_name_also_clears_a_location_that_would_still_find_nothing()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken,
            new() { ["code"] = "45378", ["npi"] = YorkvilleNpi, ["provider"] = "mercy", ["county"] = "Queens" });

        var hint = Hint(result, "provider_name_no_match");
        hint.GetProperty("message").GetString().Should().Be("NPI 1174873780 has rates for CPT 45378, but not under a name containing \"mercy\".");
        Links(hint).Should().Equal(("Clear the name and location filters", """{"provider":null,"county":null}"""));
    }

    [Fact(Timeout = 60_000)]
    public async Task Hints_about_the_provider_name_the_location_too()
    {
        var ct = TestContext.Current.CancellationToken;
        var hidden = await SearchAsync(ct, new() { ["code"] = "45378", ["county"] = "Albany", ["feeType"] = "facility" });
        var similar = await SearchAsync(ct, new() { ["code"] = "4537", ["county"] = "Manhattan" });

        Hint(hidden, "filters_hide_results").GetProperty("message").GetString().Should()
            .StartWith("This code has rates for providers in Albany County, but none match the filters (facility fee).");
        var codes = Hint(similar, "code_not_found");
        codes.GetProperty("message").GetString().Should().EndWith("Codes that start the same way, for providers in Manhattan:");
        codes.GetProperty("links")[0].GetProperty("query").TryGetProperty("county", out _).Should().BeFalse("the link keeps the location");
    }

    [Fact(Timeout = 60_000)]
    public async Task A_code_type_link_keeps_the_location_only_where_it_has_rates()
    {
        var ct = TestContext.Current.CancellationToken;
        var brooklyn = Hint(await SearchAsync(ct, new() { ["code"] = "29881", ["codeType"] = "HCPCS", ["county"] = "Brooklyn" }), "code_type_mismatch");
        var queens = Hint(await SearchAsync(ct, new() { ["code"] = "29881", ["codeType"] = "HCPCS", ["county"] = "Queens" }), "code_type_mismatch");

        var kept = brooklyn.GetProperty("links").EnumerateArray().Single();
        kept.GetProperty("label").GetString().Should().Be("Search CPT 29881");
        kept.GetProperty("query").TryGetProperty("county", out _).Should().BeFalse();
        var cleared = queens.GetProperty("links").EnumerateArray().Single();
        cleared.GetProperty("label").GetString().Should().Be("Search CPT 29881 for all providers");
        cleared.GetProperty("query").GetProperty("county").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(Timeout = 60_000)]
    public async Task Revenue_code_hints_count_only_the_providers_in_the_location()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken, new() { ["code"] = "0490", ["county"] = "Manhattan" });

        var hint = Hint(result, "revenue_code_procedures");
        hint.GetProperty("message").GetString().Should().Contain("under revenue code 0490 (Ambulatory surgery) for providers in Manhattan (");
        hint.GetProperty("links")[0].GetProperty("query").TryGetProperty("county", out _).Should().BeFalse("the links keep the location");
    }

    [Fact(Timeout = 60_000)]
    public async Task Links_that_keep_only_the_npi_clear_the_location()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken,
            new() { ["code"] = "45378", ["npi"] = YorkvilleNpi, ["ein"] = "113471596", ["county"] = "Manhattan" });

        var link = Hint(result, "npi_other_ein").GetProperty("links").EnumerateArray().Single();
        link.GetProperty("label").GetString().Should().Be("Search this NPI without the EIN");
        link.GetProperty("query").GetProperty("county").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(Timeout = 60_000)]
    public async Task The_search_echoes_the_specialty_and_location_as_typed_and_the_place_matched()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken,
            new() { ["code"] = "45378", ["specialty"] = " Gastro ", ["county"] = "MANHATTAN" });

        result.GetProperty("query").GetProperty("specialty").GetString().Should().Be("Gastro");
        result.GetProperty("query").GetProperty("county").GetString().Should().Be("MANHATTAN");
        result.GetProperty("query").GetProperty("countyPlace").GetString().Should().Be("Manhattan");
        result.GetProperty("rows").GetProperty("total").GetInt32().Should().BePositive("matching ignores case");
    }

    [Theory(Timeout = 60_000)]
    [InlineData("Kings County", "Brooklyn")]
    [InlineData("the bronx", "Bronx")]
    [InlineData("NYC", "New York City")]
    [InlineData("Delaware", "Delaware County")]
    [InlineData("Manhatan", null)]
    public async Task The_place_a_location_matched_is_echoed(string typed, string? place)
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken, new() { ["code"] = "45378", ["county"] = typed });

        var echoed = result.GetProperty("query").GetProperty("countyPlace");
        (echoed.ValueKind == JsonValueKind.Null ? null : echoed.GetString()).Should().Be(place);
    }

    private async Task<JsonElement> SearchAsync(CancellationToken cancellationToken, Dictionary<string, string> query)
    {
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(cancellationToken);
        var url = "/api/v1/rates?" + string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        using var response = await client.GetAsync(url, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    private static Dictionary<string, string> Parse(string search) =>
        search.Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));

    private static JsonElement Hint(JsonElement result, string kind) =>
        result.GetProperty("hints").EnumerateArray().Single(h => h.GetProperty("kind").GetString() == kind);

    private static List<(string Label, string Query)> Links(JsonElement hint) =>
        hint.GetProperty("links").EnumerateArray()
            .Select(link => (link.GetProperty("label").GetString()!, link.GetProperty("query").GetRawText()))
            .ToList();
}
