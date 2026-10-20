using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PriceExplorer.Tests.Support;
using static PriceExplorer.Tests.Support.TicFileBuilder;

namespace PriceExplorer.Tests.Api;

/// <summary>
/// Categories (CMS RBCS, code-range fallback), CMS HCPCS names, dose units and the level ladder. The real Ambetter slice
/// (45378, 00812, J0585, 99214, revenue codes 0450/0490) plus a synthetic Essential Plan file: six doctors' offices billing
/// 99212–99214 (two of them 99215 too), a facility drug charge for J2704 that names the drug only in its notes, and an
/// emergency room rate under revenue code 0450 alone.
/// </summary>
public sealed class CodeSetApiTests(CodeSetApiTests.Factory factory) : IClassFixture<CodeSetApiTests.Factory>
{
    public sealed class Factory() : ExplorerApiFactory(http => http.ServeJson(FixtureHttpHandler.EssentialUrl, SyntheticFile), null, null);

    private static readonly string[] Offices = ["Alder", "Birch", "Cedar", "Dogwood", "Elm", "Fir"];

    private static string Office(int index) => $$"""
        { "provider_group_id": {{21 + index}}, "network_name": [], "provider_groups": [
          { "npi": [{{1003000126 + index * 10}}], "tin": { "type": "ein", "value": "5{{index}}-100000{{index}}", "business_name": "{{Offices[index]}} Family Practice" } } ] }
        """;

    private static string Visit(string code, int office, decimal rate) =>
        Item("CPT", code, "OFFICE VISIT", $"[{21 + office}]",
            Price(rate: rate.ToString(System.Globalization.CultureInfo.InvariantCulture), serviceCode: "[\"11\"]", billingClass: "professional"));

    private static readonly string SyntheticFile = File(
        Enumerable.Range(0, Offices.Length).Select(Office),
        [
            .. Enumerable.Range(0, Offices.Length).Select(i => Visit("99212", i, 50 + i)),
            .. Enumerable.Range(0, Offices.Length).Select(i => Visit("99213", i, 80 + i * 2)),
            .. Enumerable.Range(0, Offices.Length).Select(i => Visit("99214", i, 120 + i * 3)),
            Visit("99215", 0, 170),
            Visit("99215", 1, 180),
            // A facility drug charge: the drug code is only in the notes, so the plan never names it.
            Item("RC", "0636", "DRUGS REQUIRING DETAILED CODING", "[21]", Price(rate: "12.50", additionalInformation: "J2704")),
            // A facility rate under a revenue code alone, with no procedure code: RBCS has no class for it.
            Item("RC", "0450", "EMERGENCY ROOM", "[22]", Price(rate: "500.00", serviceCode: "[\"23\"]")),
        ]);

    [Fact(Timeout = 60_000)]
    public async Task Search_results_carry_the_cms_category_and_fall_back_to_code_ranges()
    {
        var ct = TestContext.Current.CancellationToken;
        var colonoscopy = (await GetAsync("/api/v1/rates?code=45378", ct)).GetProperty("code").GetProperty("classification");
        var facility = (await GetAsync("/api/v1/rates?code=0450&codeType=RC", ct)).GetProperty("code").GetProperty("classification");

        colonoscopy.GetProperty("key").GetString().Should().Be("PG004");
        colonoscopy.GetProperty("category").GetString().Should().Be("Procedures");
        colonoscopy.GetProperty("subcategory").GetString().Should().Be("Digestive/Gastrointestinal");
        colonoscopy.GetProperty("family").GetString().Should().Be("Lower GI Endoscopy - Other");
        colonoscopy.GetProperty("source").GetString().Should().Be("cms_rbcs");
        facility.GetProperty("key").GetString().Should().Be("facility");
        facility.GetProperty("source").GetString().Should().Be("code_ranges");
    }

    [Fact(Timeout = 60_000)]
    public async Task A_drug_the_plan_never_names_gets_cms_hcpcs_name_and_its_dose_unit()
    {
        var code = (await GetAsync("/api/v1/rates?code=J2704", TestContext.Current.CancellationToken)).GetProperty("code");

        code.GetProperty("name").GetString().Should().Be("Injection, propofol, 10 mg");
        code.GetProperty("descriptionSource").GetString().Should().Be("cms_hcpcs");
        code.GetProperty("category").GetProperty("key").GetString().Should().Be("drugs");
        code.GetProperty("doseUnit").GetProperty("amount").GetDecimal().Should().Be(10m);
        code.GetProperty("doseUnit").GetProperty("unit").GetString().Should().Be("mg");
    }

    [Fact(Timeout = 60_000)]
    public async Task The_plans_own_name_wins_and_a_drug_unit_comes_from_cms_wording()
    {
        var code = (await GetAsync("/api/v1/rates?code=J0585", TestContext.Current.CancellationToken)).GetProperty("code");

        code.GetProperty("descriptionSource").GetString().Should().Be("payer_file");
        code.GetProperty("doseUnit").GetProperty("text").GetString().Should().Be("1 unit");
    }

    [Fact(Timeout = 60_000)]
    public async Task The_category_tree_counts_codes_and_rates_down_to_families()
    {
        var ct = TestContext.Current.CancellationToken;
        var tree = (await GetAsync("/api/v1/categories", ct)).GetProperty("categories").EnumerateArray().ToList();

        var procedures = tree.Single(node => node.GetProperty("key").GetString() == "P");
        var digestive = procedures.GetProperty("children").EnumerateArray().Single(node => node.GetProperty("key").GetString() == "PG");
        var endoscopy = digestive.GetProperty("children").EnumerateArray().Single(node => node.GetProperty("key").GetString() == "PG004");
        endoscopy.GetProperty("label").GetString().Should().Be("Lower GI Endoscopy - Other");
        endoscopy.GetProperty("codes").GetInt32().Should().BePositive();
        digestive.GetProperty("rates").GetInt64().Should().BeGreaterThanOrEqualTo(endoscopy.GetProperty("rates").GetInt64());

        var visits = tree.Single(node => node.GetProperty("key").GetString() == "E");
        visits.GetProperty("label").GetString().Should().Be("Visits and care management");
        var office = visits.GetProperty("children").EnumerateArray().Single(node => node.GetProperty("key").GetString() == "EV")
            .GetProperty("children").EnumerateArray().Single(node => node.GetProperty("key").GetString() == "EV001");
        office.GetProperty("codes").GetInt32().Should().Be(4, "99212 to 99215 are in the synthetic file, 99214 in the real slice too");

        // Codes RBCS doesn't list come after the RBCS categories, by their code range.
        var sources = tree.Select(node => node.GetProperty("source").GetString()).ToList();
        sources.LastIndexOf("cms_rbcs").Should().BeLessThan(sources.IndexOf("code_ranges"));
        tree.Should().Contain(node => node.GetProperty("key").GetString() == "facility" && node.GetProperty("source").GetString() == "code_ranges");
    }

    [Fact(Timeout = 60_000)]
    public async Task The_code_typeahead_takes_a_category_and_can_list_it_without_text()
    {
        var ct = TestContext.Current.CancellationToken;
        var family = await GetAsync("/api/v1/codes?category=PG004&limit=50", ct);
        var drugs = await GetAsync("/api/v1/codes?q=propofol&category=RI", ct);
        var none = await GetAsync("/api/v1/codes?q=propofol&category=PG", ct);

        family.EnumerateArray().Select(code => code.GetProperty("code").GetString()).Should().Contain("45378");
        family.EnumerateArray().Should().OnlyContain(code => code.GetProperty("category").GetString() == "Procedures · Digestive/Gastrointestinal");
        drugs.EnumerateArray().Select(code => code.GetProperty("code").GetString()).Should().Equal("J2704");
        drugs[0].GetProperty("name").GetString().Should().Be("Injection, propofol, 10 mg", "CMS's name is searchable too");
        none.GetArrayLength().Should().Be(0);
    }

    [Theory(Timeout = 60_000)]
    [InlineData("/api/v1/codes?category=P%20G")]
    [InlineData("/api/v1/codes")]
    [InlineData("/api/v1/rates/levels?code=45378")]
    [InlineData("/api/v1/rates/levels")]
    public async Task Bad_category_or_level_requests_are_400(string url)
    {
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(TestContext.Current.CancellationToken);
        using var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(Timeout = 60_000)]
    public async Task The_level_ladder_lines_up_each_level_like_for_like_with_the_search_cards()
    {
        var ct = TestContext.Current.CancellationToken;
        var ladder = await GetAsync("/api/v1/rates/levels?code=99213", ct);

        ladder.GetProperty("family").GetString().Should().Be("Office visit, established patient");
        ladder.GetProperty("codes").EnumerateArray().Select(c => (c.GetProperty("code").GetString(), c.GetProperty("level").GetInt32()))
            .Should().Equal(("99211", 1), ("99212", 2), ("99213", 3), ("99214", 4), ("99215", 5));

        // The synthetic offices: levels 2–4 with six doctors each come first; level 5 has two (too few for statistics).
        var line = ladder.GetProperty("lines")[0];
        line.GetProperty("key").GetString().Should().Be("essential-plan|CPT|professional|office||negotiated");
        line.GetProperty("settingLabel").GetString().Should().Be("Doctor's office");
        var steps = line.GetProperty("levels").EnumerateArray().ToList();
        steps.Select(step => (step.GetProperty("level").GetInt32(), step.GetProperty("providerCount").GetInt32(), step.GetProperty("statsLevel").GetString()))
            .Should().Equal((2, 6, "basic"), (3, 6, "basic"), (4, 6, "basic"), (5, 2, "list"));

        // Each step is that level's search card, number for number.
        var search = await GetAsync("/api/v1/rates?code=99213", ct);
        var card = search.GetProperty("groups").EnumerateArray().Single(group => group.GetProperty("key").GetString() == steps[1].GetProperty("groupKey").GetString());
        steps[1].GetProperty("stats").GetRawText().Should().Be(card.GetProperty("stats").GetRawText());
        steps[1].GetProperty("stats").GetProperty("median").GetDecimal().Should().Be(85m);
    }

    [Fact(Timeout = 60_000)]
    public async Task Without_the_code_set_files_codes_keep_the_plans_names_and_the_code_range_categories()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Directory.CreateTempSubdirectory("pte-codes-").FullName;
        var damaged = Path.Combine(folder, "rbcs.csv.gz");
        await System.IO.File.WriteAllBytesAsync(damaged, [0x1F, 0x8B, 0x08, 0x00, 0xDE, 0xAD, 0xBE, 0xEF], ct);
        try
        {
            await using var bare = ExplorerApiFactory.With(settings: new Dictionary<string, string?>
            {
                ["Reference:HcpcsNamesPath"] = Path.Combine(folder, "missing.csv.gz"),
                ["Reference:RbcsPath"] = damaged,
            });
            using var client = bare.CreateClient();
            await bare.WaitForIngestionAsync(ct);

            var code = (await client.GetFromJsonAsync<JsonElement>("/api/v1/rates?code=45378", ct)).GetProperty("code");
            var tree = await client.GetFromJsonAsync<JsonElement>("/api/v1/categories", ct);

            code.GetProperty("classification").GetProperty("source").GetString().Should().Be("code_ranges");
            code.GetProperty("classification").GetProperty("key").GetString().Should().Be("surgery");
            code.GetProperty("descriptionSource").GetString().Should().Be("payer_file");
            tree.GetProperty("categories").EnumerateArray().Should().OnlyContain(node => node.GetProperty("source").GetString() == "code_ranges");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private async Task<JsonElement> GetAsync(string url, CancellationToken ct)
    {
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);
        using var response = await client.GetAsync(url, ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }
}
