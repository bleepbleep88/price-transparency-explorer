using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PriceExplorer.Tests.Support;

namespace PriceExplorer.Tests.Api;

/// <summary>
/// Uses the real bundled provider directory (reference/provider_directory.csv.gz) with the real fixture slices. Yorkville
/// Endoscopy (NPI 1174873780) is a Manhattan surgery center with one CPT 45378 facility rate.
/// </summary>
public sealed class ProviderDirectoryApiTests(ExplorerApiFactory factory) : IClassFixture<ExplorerApiFactory>
{
    [Fact(Timeout = 60_000)]
    public async Task Rows_show_who_the_npis_behind_a_rate_are()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await SearchAsync("code=45378&npi=1174873780", ct);

        var profile = result.GetProperty("rows").GetProperty("items")[0].GetProperty("profile");
        profile.GetProperty("specialties")[0].GetProperty("specialty").GetString().Should().Be("Ambulatory Surgical Clinic/Center");
        profile.GetProperty("locations").EnumerateArray().Select(l => l.GetString()).Should().Equal("Manhattan, NY");
        profile.GetProperty("individuals").GetInt32().Should().Be(0);
    }

    [Fact(Timeout = 60_000)]
    public async Task Location_and_specialty_filters_narrow_rows_to_matching_providers()
    {
        var ct = TestContext.Current.CancellationToken;
        var manhattan = await SearchAsync("code=45378&feeType=facility&county=manhattan", ct);
        var putnam = await SearchAsync("code=45378&feeType=facility&county=Putnam", ct);
        var surgeryCenters = await SearchAsync("code=45378&specialty=ambulatory%20surgical", ct);

        Names(manhattan).Should().Contain(name => name.StartsWith("Yorkville Endoscopy", StringComparison.Ordinal));
        Names(putnam).Should().NotBeEmpty().And.NotContain(name => name.StartsWith("Yorkville Endoscopy", StringComparison.Ordinal));
        surgeryCenters.GetProperty("rows").GetProperty("items").EnumerateArray()
            .Should().NotBeEmpty().And.OnlyContain(row => row.GetProperty("profile").GetProperty("specialties").EnumerateArray()
                .Any(s => s.GetProperty("specialty").GetString() == "Ambulatory Surgical Clinic/Center"));
    }

    [Fact(Timeout = 60_000)]
    public async Task With_both_filters_one_npi_must_have_the_specialty_and_practice_there()
    {
        var ct = TestContext.Current.CancellationToken;
        const string chinatown = "Chinatown True Care Medical PLLC dba RenDr Care";

        // Its gastroenterologists practice in Brooklyn, Nassau and Queens; its Westchester NPI isn't one.
        var gastro = await SearchAsync("code=45378&specialty=gastro&pageSize=100", ct);
        var westchester = await SearchAsync("code=45378&county=Westchester&pageSize=100", ct);
        var both = await SearchAsync("code=45378&specialty=gastro&county=Westchester&pageSize=100", ct);

        Names(gastro).Should().Contain(chinatown);
        Names(westchester).Should().Contain(chinatown);
        Names(both).Should().NotContain(chinatown);
    }

    [Fact(Timeout = 60_000)]
    public async Task A_picked_specialty_finds_that_specialty_only()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);

        var suggestions = await client.GetFromJsonAsync<JsonElement>("/api/v1/specialties?q=gastroenterology", ct);
        var picked = suggestions.EnumerateArray().First(s => s.GetProperty("specialty").GetString() == "Gastroenterology Physician");
        var result = await SearchAsync("code=45378&specialty=Gastroenterology%20Physician&pageSize=100", ct);

        // Matching specialties are listed first, so each row leads with the one picked.
        var rows = result.GetProperty("rows").GetProperty("items").EnumerateArray().ToList();
        rows.Should().NotBeEmpty().And.OnlyContain(row =>
            row.GetProperty("profile").GetProperty("specialties")[0].GetProperty("specialty").GetString() == "Gastroenterology Physician");
        picked.GetProperty("npis").GetInt32().Should().BePositive();
    }

    [Theory(Timeout = 60_000)]
    // Typed words match from the start of a word: never "Neurology", never "Surgical" for GI.
    [InlineData("urology", "Neurology")]
    [InlineData("GI", "Surgical")]
    [InlineData("physician assistant", "Nurse Practitioner")]
    public async Task Typed_specialty_words_match_from_the_start_of_a_word(string typed, string never)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);

        var suggestions = await client.GetFromJsonAsync<JsonElement>($"/api/v1/specialties?q={Uri.EscapeDataString(typed)}&limit=50", ct);

        suggestions.EnumerateArray().Select(s => s.GetProperty("specialty").GetString()!)
            .Should().NotBeEmpty().And.NotContain(name => name.Contains(never, StringComparison.OrdinalIgnoreCase));
    }

    [Theory(Timeout = 60_000)]
    [InlineData("cardiologist", "Cardiovascular Disease Physician")]
    [InlineData("orthopaedic surgeon", "Orthopaedic Surgery Physician")]
    [InlineData("ENT", "Otolaryngology Physician")]
    [InlineData("ob/gyn", "Obstetrics & Gynecology Physician")]
    [InlineData("anesthesia", "Anesthesiology Physician")]
    public async Task Common_specialty_words_find_the_registry_name(string typed, string name)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);

        var suggestions = await client.GetFromJsonAsync<JsonElement>($"/api/v1/specialties?q={Uri.EscapeDataString(typed)}&limit=50", ct);

        suggestions.EnumerateArray().Select(s => s.GetProperty("specialty").GetString()).Should().Contain(name);
    }

    [Fact(Timeout = 60_000)]
    public async Task Location_names_match_with_or_without_county_borough_or_the()
    {
        var ct = TestContext.Current.CancellationToken;
        var bronx = await SearchAsync("code=45378&county=Bronx&pageSize=100", ct);
        var theBronx = await SearchAsync("code=45378&county=The%20Bronx&pageSize=100", ct);
        var bronxCounty = await SearchAsync("code=45378&county=Bronx%20County&pageSize=100", ct);
        var manhattan = await SearchAsync("code=45378&county=Manhattan&pageSize=100", ct);
        var nyc = await SearchAsync("code=45378&county=NYC&pageSize=100", ct);

        Names(bronx).Should().NotBeEmpty();
        Names(theBronx).Should().Equal(Names(bronx));
        Names(bronxCounty).Should().Equal(Names(bronx));
        Total(nyc).Should().BeGreaterThan(Total(manhattan)).And.BeGreaterThan(Total(bronx));
    }

    [Fact(Timeout = 60_000)]
    public async Task Filters_pick_providers_but_statistics_stay_market_wide()
    {
        var ct = TestContext.Current.CancellationToken;
        var all = await SearchAsync("code=45378", ct);
        var queens = await SearchAsync("code=45378&county=Queens", ct);

        var stats = all.GetProperty("groups").EnumerateArray().ToDictionary(g => g.GetProperty("key").GetString()!, g => g.GetProperty("stats").GetRawText());
        foreach (var group in queens.GetProperty("groups").EnumerateArray())
        {
            group.GetProperty("stats").GetRawText().Should().Be(stats[group.GetProperty("key").GetString()!]);
            group.GetProperty("matchedProviders").GetInt32().Should().BePositive();
        }

        queens.GetProperty("rows").GetProperty("total").GetInt32().Should().BeLessThan(all.GetProperty("rows").GetProperty("total").GetInt32());
        queens.GetProperty("query").GetProperty("county").GetString().Should().Be("Queens");
    }

    [Fact(Timeout = 60_000)]
    public async Task With_a_location_filter_each_row_leads_with_a_place_there()
    {
        var ct = TestContext.Current.CancellationToken;
        var rows = (await SearchAsync("code=45378&county=Manhattan&pageSize=100", ct)).GetProperty("rows").GetProperty("items").EnumerateArray().ToList();
        var outsideNyc = (await SearchAsync("code=45378&county=Ulster", ct)).GetProperty("rows").GetProperty("items").EnumerateArray().ToList();

        rows.Should().NotBeEmpty().And.OnlyContain(row => row.GetProperty("profile").GetProperty("locations")[0].GetString() == "Manhattan, NY");
        outsideNyc.Should().NotBeEmpty().And.OnlyContain(row =>
            row.GetProperty("profile").GetProperty("locations")[0].GetString()!.EndsWith(", NY · Ulster County", StringComparison.Ordinal));
    }

    [Fact(Timeout = 60_000)]
    public async Task Each_rows_profile_counts_the_same_npis_as_the_row_and_its_npi_list()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await SearchAsync("code=45378&pageSize=30", ct);
        using var client = factory.CreateClient();

        foreach (var row in result.GetProperty("rows").GetProperty("items").EnumerateArray())
        {
            var rateIds = row.GetProperty("rateIds").EnumerateArray().Select(id => id.GetInt64()).ToList();
            var also = string.Concat(rateIds.Skip(1).Select(id => $"&alsoRate={id}"));
            var npis = await client.GetFromJsonAsync<JsonElement>(
                $"/api/v1/rates/{rateIds[0]}/npis?provider={Uri.EscapeDataString(row.GetProperty("providerKey").GetString()!)}{also}&pageSize=1", ct);

            row.GetProperty("profile").GetProperty("npis").GetInt32().Should().Be(row.GetProperty("npiCount").GetInt32());
            npis.GetProperty("total").GetInt32().Should().Be(row.GetProperty("npiCount").GetInt32());
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task A_deactivated_npi_is_flagged_and_not_counted_as_a_clinician()
    {
        var ct = TestContext.Current.CancellationToken;
        // Edgar Lichstein MD lists one NPI for CPT 99214, and the registry has deactivated it.
        var row = (await SearchAsync("code=99214&provider=lichstein", ct)).GetProperty("rows").GetProperty("items")[0];

        var profile = row.GetProperty("profile");
        profile.GetProperty("deactivated").GetInt32().Should().Be(1);
        profile.GetProperty("individuals").GetInt32().Should().Be(0);
        row.GetProperty("flags").EnumerateArray().Select(f => f.GetProperty("message").GetString())
            .Should().Equal("The only NPI listed for this rate is deactivated in the national registry.");
    }

    [Fact(Timeout = 60_000)]
    public async Task Specialties_and_locations_are_offered_for_the_filters()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);

        var specialties = await client.GetFromJsonAsync<JsonElement>("/api/v1/specialties?q=anesth", ct);
        var reference = await client.GetFromJsonAsync<JsonElement>("/api/v1/reference", ct);

        specialties.EnumerateArray().Select(s => s.GetProperty("specialty").GetString()).Should().Contain("Anesthesiology Physician");
        reference.GetProperty("locations").EnumerateArray().Take(5).Select(l => l.GetProperty("label").GetString())
            .Should().BeEquivalentTo(["Manhattan", "Brooklyn", "Queens", "Bronx", "Staten Island"]);
        reference.GetProperty("locations").EnumerateArray().Select(l => l.GetProperty("label").GetString())
            .Should().Contain("Monroe").And.HaveCount(62, "New York has 62 counties, five of them the boroughs");
    }

    [Fact(Timeout = 60_000)]
    public async Task The_reference_lists_cite_the_registry_release_and_specialty_typeahead_needs_text()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);

        var reference = await client.GetFromJsonAsync<JsonElement>("/api/v1/reference", ct);
        using var noText = await client.GetAsync("/api/v1/specialties", ct);

        var directory = reference.GetProperty("directory");
        directory.GetProperty("release").GetString().Should().Be("October 2026");
        directory.GetProperty("npisLoaded").GetInt64().Should().BeGreaterThan(50_000);
        noText.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(Timeout = 60_000)]
    public async Task Only_new_york_counties_match_a_location()
    {
        var ct = TestContext.Current.CancellationToken;
        // Middletown Medical's NPIs practice in Orange County, NY, plus two in Bergen County, NJ.
        var orange = await SearchAsync("code=J0585&county=Orange", ct);
        var bergen = await SearchAsync("code=J0585&county=Bergen", ct);

        Names(orange).Should().Contain("Middletown Medical, PC");
        bergen.GetProperty("rows").GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact(Timeout = 60_000)]
    public async Task A_damaged_directory_file_turns_off_profiles_but_search_still_works()
    {
        var ct = TestContext.Current.CancellationToken;
        var damaged = Path.Combine(Directory.CreateTempSubdirectory("pte-dir-").FullName, "provider_directory.csv.gz");
        await File.WriteAllBytesAsync(damaged, [0x1F, 0x8B, 0x08, 0x00, 0xDE, 0xAD, 0xBE, 0xEF], ct);
        try
        {
            await using var damagedFactory = ExplorerApiFactory.With(settings: new Dictionary<string, string?> { ["Reference:ProviderDirectoryPath"] = damaged });
            using var client = damagedFactory.CreateClient();
            await damagedFactory.WaitForIngestionAsync(ct);

            var result = await client.GetFromJsonAsync<JsonElement>("/api/v1/rates?code=45378", ct);
            var filtered = await client.GetFromJsonAsync<JsonElement>("/api/v1/rates?code=45378&county=Manhattan", ct);
            var reference = await client.GetFromJsonAsync<JsonElement>("/api/v1/reference", ct);

            result.GetProperty("rows").GetProperty("total").GetInt32().Should().BePositive();
            result.GetProperty("rows").GetProperty("items")[0].GetProperty("profile").ValueKind.Should().Be(JsonValueKind.Null);
            filtered.GetProperty("hints").EnumerateArray().Select(h => h.GetProperty("message").GetString())
                .Should().Contain("The provider registry isn't loaded, so specialty and location filters match no one.");
            reference.GetProperty("locations").GetArrayLength().Should().Be(0);
            reference.GetProperty("directory").ValueKind.Should().Be(JsonValueKind.Null);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(damaged)!, recursive: true);
        }
    }

    private async Task<JsonElement> SearchAsync(string query, CancellationToken ct)
    {
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);
        return await client.GetFromJsonAsync<JsonElement>($"/api/v1/rates?{query}", ct);
    }

    private static int Total(JsonElement result) => result.GetProperty("rows").GetProperty("total").GetInt32();

    private static List<string> Names(JsonElement result) =>
        result.GetProperty("rows").GetProperty("items").EnumerateArray().Select(r => r.GetProperty("businessName").GetString()!).ToList();
}
