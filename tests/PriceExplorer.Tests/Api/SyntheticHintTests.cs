using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PriceExplorer.Tests.Support;
using static PriceExplorer.Tests.Support.TicFileBuilder;

namespace PriceExplorer.Tests.Api;

/// <summary>
/// Hint paths the real slices can't reach: revenue codes with rates billed directly AND filed under procedure codes, and
/// an NPI listed under both a verified and a masked tax ID. The Essential Plan file is swapped for a small synthetic one.
/// </summary>
public sealed class SyntheticHintTests(SyntheticHintTests.Factory factory) : IClassFixture<SyntheticHintTests.Factory>
{
    private const string MinkowitzNpi = "1396747861";

    /// <summary>The real API with the synthetic Essential Plan file.</summary>
    public sealed class Factory() : ExplorerApiFactory(http => http.ServeJson(FixtureHttpHandler.EssentialUrl, SyntheticFile), null, null);

    private const string AlphaEndoscopy = """
        { "provider_group_id": 5, "network_name": [], "provider_groups": [
          { "npi": [1234567893], "tin": { "type": "ein", "value": "22-1111111", "business_name": "Alpha Endoscopy Center" } } ] }
        """;

    private const string BetaSurgery = """
        { "provider_group_id": 6, "network_name": [], "provider_groups": [
          { "npi": [1245319599], "tin": { "type": "ein", "value": "33-2222222", "business_name": "Beta Surgery Center" } } ] }
        """;

    private const string GammaEmergency = """
        { "provider_group_id": 8, "network_name": [], "provider_groups": [
          { "npi": [1386622339], "tin": { "type": "ein", "value": "44-3333333", "business_name": "Gamma Emergency Physicians" } } ] }
        """;

    /// <summary>Minkowitz's NPI is also listed under the masked tax ID 07-1234567.</summary>
    private const string CountyClinicWithMinkowitz = """
        { "provider_group_id": 7, "network_name": [], "provider_groups": [
          { "npi": [1396747861], "tin": { "type": "ein", "value": "07-1234567", "business_name": "County Health Clinic" } } ] }
        """;

    private static readonly string SyntheticFile = File(
        [Minkowitz, CountyClinicWholeTin, AlphaEndoscopy, BetaSurgery, CountyClinicWithMinkowitz, GammaEmergency],
        [
            // Minkowitz: one emergency room rate billed under revenue code 0450, one filed under CPT 99284.
            Item("RC", "0450", "EMERGENCY ROOM", "[1]", Price(rate: "500.00", serviceCode: "[\"23\"]")),
            Item("RC", "0450", "EMERGENCY ROOM", "[1]", Price(rate: "300.00", serviceCode: "[\"23\"]", additionalInformation: "99284")),
            // Gamma: an emergency room rate filed under CPT 99285 only.
            Item("RC", "0450", "EMERGENCY ROOM", "[8]", Price(rate: "350.00", serviceCode: "[\"23\"]", additionalInformation: "99285")),
            // Revenue code 0490: Beta bills it directly; Alpha's rate is filed under CPT 45378.
            Item("RC", "0490", "AMBULATORY SURGERY", "[6]", Price(rate: "900.00")),
            Item("RC", "0490", "AMBULATORY SURGERY", "[5]", Price(rate: "600.00", additionalInformation: "45378")),
            Item("CPT", "88305", "TISSUE EXAM BY PATHOLOGIST", "[1, 2, 7]", Price(rate: "41.50", serviceCode: "[\"11\"]", billingClass: "professional")),
        ]);

    [Fact(Timeout = 60_000)]
    public async Task A_providers_procedure_coded_rate_is_called_one_more_when_it_also_bills_the_revenue_code()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken, $"code=0450&npi={MinkowitzNpi}&product=essential-plan");

        result.GetProperty("rows").GetProperty("total").GetInt32().Should().Be(1, "the directly billed rate shows as a row");
        Message(result, "revenue_code_procedures").Should().Be(
            "One more facility rate under revenue code 0450 (Emergency room) for NPI 1396747861 is filed under the procedure code " +
            "in its notes field (CPT 99284). Search it:");
    }

    [Fact(Timeout = 60_000)]
    public async Task A_name_with_procedure_coded_rates_the_npi_lacks_is_not_called_unmatched()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken, $"code=0490&npi={MinkowitzNpi}&provider=endoscopy");

        Kinds(result).Should().NotContain("provider_name_no_match");
        result.GetProperty("hints").EnumerateArray().Select(h => h.GetProperty("message").GetString()).Should().Contain(
            "Providers whose name contains \"endoscopy\" (3 tax IDs) have 3 facility rates under revenue code 0490 (Ambulatory surgery), " +
            "none listed for NPI 1396747861. They're filed under the procedure code in the notes field (1 code). Search one to see them:");
    }

    [Fact(Timeout = 60_000)]
    public async Task A_name_matching_only_other_providers_keeps_the_way_back_to_the_npis_own_rates()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken, $"code=0450&npi={MinkowitzNpi}&provider=gamma");

        var hint = result.GetProperty("hints").EnumerateArray().Single(h => h.GetProperty("kind").GetString() == "provider_name_no_match");
        hint.GetProperty("message").GetString().Should()
            .Be("NPI 1396747861 has rates for RC 0450, but not under a name containing \"gamma\".");
        hint.GetProperty("links")[0].GetProperty("label").GetString().Should().Be("Clear the name filter");
        result.GetProperty("hints").EnumerateArray().Select(h => h.GetProperty("message").GetString())
            .Should().Contain(m => m!.StartsWith("Providers whose name contains \"gamma\" (1 tax ID) have 1 facility rate"));
    }

    [Fact(Timeout = 60_000)]
    public async Task A_wrong_code_type_link_keeps_the_npi_when_only_its_ein_is_wrong()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken, $"code=0450&codeType=CPT&npi={MinkowitzNpi}&ein=999999999");

        var link = result.GetProperty("hints").EnumerateArray().Single(h => h.GetProperty("kind").GetString() == "code_type_mismatch")
            .GetProperty("links").EnumerateArray().Single();
        link.GetProperty("label").GetString().Should().Be("Search RC 0450 for NPI 1396747861");
        link.GetProperty("query").GetProperty("ein").ValueKind.Should().Be(JsonValueKind.Null);
        link.GetProperty("query").TryGetProperty("npi", out _).Should().BeFalse("the link keeps the NPI");
    }

    [Fact(Timeout = 60_000)]
    public async Task An_npi_under_a_verified_and_a_masked_tax_id_names_each_with_its_own_tax_id()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken, $"code=0490&npi={MinkowitzNpi}");

        Message(result, "provider_no_rate").Should().StartWith(
            "NPI 1396747861 (County Health Clinic (masked tax ID) and Minkowitz Consulting Pathologists PC, EIN 11-3471596) " +
            "has no rate for RC 0490 in these files.");
    }

    [Fact(Timeout = 60_000)]
    public async Task Drill_downs_find_a_masked_tax_id_by_its_full_number_as_search_does()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await SearchAsync(ct, "code=88305&ein=071234567");
        var row = result.GetProperty("rows").GetProperty("items").EnumerateArray().Single();
        var rateId = row.GetProperty("firstRateId").GetInt64();
        using var client = factory.CreateClient();

        var byNumber = await client.GetFromJsonAsync<JsonElement>($"/api/v1/rates/{rateId}/npis?provider=071234567", ct);
        var byKey = await client.GetFromJsonAsync<JsonElement>($"/api/v1/rates/{rateId}/npis?provider={row.GetProperty("providerKey").GetString()}", ct);
        var source = await client.GetFromJsonAsync<JsonElement>($"/api/v1/rates/{rateId}/source?provider=071234567", ct);

        byNumber.GetProperty("total").GetInt32().Should().Be(byKey.GetProperty("total").GetInt32()).And.BePositive();
        source.GetProperty("providerGroups")[0].GetProperty("providerMatch").GetBoolean().Should().BeTrue();
    }

    private async Task<JsonElement> SearchAsync(CancellationToken cancellationToken, string query)
    {
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(cancellationToken);
        using var response = await client.GetAsync($"/api/v1/rates?{query}", cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, query);
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    private static List<string?> Kinds(JsonElement result) =>
        result.GetProperty("hints").EnumerateArray().Select(h => h.GetProperty("kind").GetString()).ToList();

    private static string? Message(JsonElement result, string kind) =>
        result.GetProperty("hints").EnumerateArray().Single(h => h.GetProperty("kind").GetString() == kind).GetProperty("message").GetString();
}
