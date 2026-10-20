using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PriceExplorer.Tests.Support;

namespace PriceExplorer.Tests.Api;

/// <summary>
/// Hints over the real Fidelis slices (see Fixtures/README.md). In these slices every revenue code 0490 rate is filed
/// under a procedure code: Yorkville Endoscopy (EIN 46-0857425, NPI 1174873780) has one, in Ambetter, as CPT 45378.
/// </summary>
public sealed class SearchHintTests(ExplorerApiFactory factory) : IClassFixture<ExplorerApiFactory>
{
    private const string YorkvilleEin = "460857425";
    private const string YorkvilleNpi = "1174873780";

    /// <summary>Searches that produce hints with links, across every kind of hint.</summary>
    public static TheoryData<string> SearchesWithLinks =>
    [
        "code=0490",
        "code=0490&codeType=CPT",
        "code=29881&codeType=HCPCS",
        $"code=29881&codeType=HCPCS&npi={YorkvilleNpi}",
        "code=45379",
        "code=4537",
        $"code=45399&npi={YorkvilleNpi}",
        "code=45399&npi=1649596081",
        $"code=0490&ein={YorkvilleEin}&product=essential-plan",
        $"code=0490&npi={YorkvilleNpi}&ein=113471596",
        $"code=0490&ein={YorkvilleEin}&provider=zzz",
        "code=0490&provider=endoscopy",
        $"code=45378&npi={YorkvilleNpi}&setting=office",
        $"code=45378&ein={YorkvilleEin}&provider=Mercy",
        $"code=45378&npi={YorkvilleNpi}&ein=11-3471596",
        "code=0450",
        "code=450&codeType=CPT",
    ];

    [Theory(Timeout = 120_000)]
    [MemberData(nameof(SearchesWithLinks))]
    public async Task Every_hint_link_finds_rates(string search)
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
    }

    [Fact(Timeout = 60_000)]
    public async Task A_revenue_code_searched_as_cpt_points_to_the_procedure_codes_its_rates_are_filed_under()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken, new() { ["code"] = "0490", ["codeType"] = "CPT" });

        Kinds(result).Should().Equal("revenue_code_procedures");
        var hint = Hint(result, "revenue_code_procedures");
        hint.GetProperty("message").GetString().Should().StartWith("No CPT rates for 0490. All ").And.Contain("revenue code 0490 (Ambulatory surgery)");
        LinkCodes(hint).Should().Contain(["45378", "29881"]);
    }

    [Fact(Timeout = 60_000)]
    public async Task A_providers_revenue_code_rates_in_another_product_are_pointed_to_never_called_missing()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken,
            new() { ["code"] = "0490", ["ein"] = YorkvilleEin, ["product"] = "essential-plan" });

        Kinds(result).Should().Equal("revenue_code_procedures");
        var hint = Hint(result, "revenue_code_procedures");
        hint.GetProperty("message").GetString().Should().Be(
            "EIN 46-0857425 has 1 facility rate under revenue code 0490 (Ambulatory surgery) filed under the procedure code in the notes " +
            "field (CPT 45378), in Ambetter from Fidelis Care. Search it:");
        var link = hint.GetProperty("links").EnumerateArray().Single().GetProperty("query");
        link.GetProperty("code").GetString().Should().Be("45378");
        link.GetProperty("product").ValueKind.Should().Be(JsonValueKind.Null);
        link.TryGetProperty("ein", out _).Should().BeFalse("the link keeps the EIN");
    }

    [Fact(Timeout = 60_000)]
    public async Task An_npi_whose_revenue_code_rates_sit_under_another_tax_id_is_told_where()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken,
            new() { ["code"] = "0490", ["npi"] = YorkvilleNpi, ["ein"] = "113471596" });

        Kinds(result).Should().Equal("revenue_code_procedures");
        var hint = Hint(result, "revenue_code_procedures");
        hint.GetProperty("message").GetString().Should().Be(
            "NPI 1174873780 has 1 facility rate under revenue code 0490 (Ambulatory surgery) filed under the " +
            "procedure code in the notes field (CPT 45378), listed under Yorkville Endoscopy, LLC, EIN 46-0857425, not under " +
            "EIN 11-3471596. Contracts are signed per tax ID, so the NPI and EIN must match. Search it:");
        hint.GetProperty("links")[0].GetProperty("query").GetProperty("ein").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(Timeout = 60_000)]
    public async Task A_name_that_hides_a_providers_revenue_code_rates_is_named_as_the_reason()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken,
            new() { ["code"] = "0490", ["ein"] = YorkvilleEin, ["provider"] = "zzz" });

        var hint = Hint(result, "revenue_code_procedures");
        hint.GetProperty("message").GetString().Should().StartWith(
            "EIN 46-0857425 has 1 facility rate under revenue code 0490 (Ambulatory surgery) filed under the procedure code in the " +
            "notes field (CPT 45378), but none under a name containing \"zzz\".");
        hint.GetProperty("links")[0].GetProperty("query").GetProperty("provider").ValueKind.Should().Be(JsonValueKind.Null);
        Kinds(result).Should().NotContain("provider_no_rate");
    }

    [Fact(Timeout = 60_000)]
    public async Task A_name_filter_matching_several_tax_ids_is_not_called_one_provider()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken, new() { ["code"] = "0490", ["provider"] = "endoscopy" });

        var message = Hint(result, "revenue_code_procedures").GetProperty("message").GetString();
        message.Should().Contain("for providers whose name contains \"endoscopy\" (2 tax IDs)").And.NotContain("This provider");
    }

    [Fact(Timeout = 60_000)]
    public async Task A_wrong_code_type_for_a_provider_without_that_code_offers_it_for_all_providers_and_says_why()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken,
            new() { ["code"] = "29881", ["codeType"] = "HCPCS", ["npi"] = YorkvilleNpi });

        Kinds(result).Should().Equal("code_facility_only", "code_type_mismatch", "provider_no_rate");
        var link = Hint(result, "code_type_mismatch").GetProperty("links").EnumerateArray().Single();
        link.GetProperty("label").GetString().Should().Be("Search CPT 29881 for all providers");
        link.GetProperty("query").GetProperty("npi").ValueKind.Should().Be(JsonValueKind.Null);
        Hint(result, "provider_no_rate").GetProperty("message").GetString().Should()
            .StartWith("NPI 1174873780 (Yorkville Endoscopy, LLC, EIN 46-0857425) has no rate for CPT 29881 in these files.");
    }

    [Fact(Timeout = 60_000)]
    public async Task A_selected_group_the_provider_isnt_in_is_explained()
    {
        var ct = TestContext.Current.CancellationToken;
        var all = await SearchAsync(ct, new() { ["code"] = "45378" });
        var essentialAsc = all.GetProperty("groups").EnumerateArray().Single(g => g.GetProperty("productKey").GetString() == "essential-plan"
            && g.GetProperty("feeType").GetString() == "professional" && g.GetProperty("settingKey").GetString() == "asc").GetProperty("key").GetString()!;

        var result = await SearchAsync(ct, new() { ["code"] = "45378", ["npi"] = YorkvilleNpi, ["group"] = essentialAsc });

        result.GetProperty("rows").GetProperty("total").GetInt32().Should().Be(0);
        var hint = Hint(result, "group_not_in_results");
        hint.GetProperty("message").GetString().Should().StartWith("The comparison group you picked has no rates for NPI 1174873780");
        hint.GetProperty("links")[0].GetProperty("query").GetProperty("group").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(Timeout = 60_000)]
    public async Task Similar_codes_start_like_the_code_as_typed()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken, new() { ["code"] = "4537" });

        LinkCodes(Hint(result, "code_not_found")).Should().Equal("45378");
    }

    [Fact(Timeout = 60_000)]
    public async Task Similar_codes_prefer_the_filtered_providers_own_and_otherwise_drop_the_provider()
    {
        var ct = TestContext.Current.CancellationToken;

        var own = Hint(await SearchAsync(ct, new() { ["code"] = "45399", ["npi"] = YorkvilleNpi }), "code_not_found");
        var others = Hint(await SearchAsync(ct, new() { ["code"] = "45399", ["npi"] = "1649596081" }), "code_not_found");

        own.GetProperty("message").GetString().Should().EndWith("Codes that start the same way, for NPI 1174873780:");
        own.GetProperty("links")[0].GetProperty("query").TryGetProperty("npi", out _).Should().BeFalse("the link keeps the NPI");
        others.GetProperty("message").GetString().Should().EndWith("Codes that start the same way, none of them for NPI 1649596081:");
        others.GetProperty("links")[0].GetProperty("query").GetProperty("npi").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory(Timeout = 60_000)]
    [InlineData("490", "MS-DRG", "No MS-DRG rates for 490. DRGs price inpatient stays")]
    [InlineData("45378", "MS-DRG", "No MS-DRG rates for 45378. DRGs price inpatient stays")]
    [InlineData("45378", "APR-DRG", "No APR-DRG rates for 45378. DRGs price inpatient stays")]
    [InlineData("45378", "APC", "No APC rates for 45378. These files list rates by CPT, HCPCS and revenue code only.")]
    public async Task A_code_type_with_its_own_numbering_never_matches_these_files_codes(string code, string codeType, string message)
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken, new() { ["code"] = code, ["codeType"] = codeType });

        result.GetProperty("code").ValueKind.Should().Be(JsonValueKind.Null, "the CPT code with the same digits isn't this code");
        result.GetProperty("groups").GetArrayLength().Should().Be(0);
        var hint = result.GetProperty("hints").EnumerateArray().Should().ContainSingle().Subject;
        hint.GetProperty("kind").GetString().Should().Be("code_not_found");
        hint.GetProperty("message").GetString().Should().StartWith(message);
    }

    [Fact(Timeout = 60_000)]
    public async Task A_revenue_code_number_typed_as_hcpcs_still_finds_the_providers_procedure_codes()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken,
            new() { ["code"] = "0490", ["codeType"] = "HCPCS", ["npi"] = YorkvilleNpi });

        Kinds(result).Should().Equal("revenue_code_procedures");
        Hint(result, "revenue_code_procedures").GetProperty("message").GetString().Should()
            .StartWith("The only facility rate under revenue code 0490 (Ambulatory surgery) for NPI 1174873780 is filed under the procedure code");
    }

    [Fact(Timeout = 60_000)]
    public async Task A_name_that_matches_the_npis_real_tax_id_is_not_blamed()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken,
            new() { ["code"] = "0490", ["npi"] = YorkvilleNpi, ["ein"] = "113471596", ["provider"] = "yorkville" });

        var message = Hint(result, "revenue_code_procedures").GetProperty("message").GetString();
        message.Should().Contain("listed under Yorkville Endoscopy, LLC, EIN 46-0857425, not under EIN 11-3471596")
            .And.NotContain("but none under a name");
    }

    [Fact(Timeout = 60_000)]
    public async Task A_product_without_rates_for_the_code_is_named_by_its_label()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken, new() { ["code"] = "29881", ["product"] = "essential-plan" });

        Hint(result, "filters_hide_results").GetProperty("message").GetString().Should()
            .StartWith("This code has rates, but none match the filters (Essential Plan).");
    }

    [Fact(Timeout = 60_000)]
    public async Task An_npi_with_a_leading_zero_is_echoed_and_quoted_as_typed()
    {
        var result = await SearchAsync(TestContext.Current.CancellationToken, new() { ["code"] = "45378", ["npi"] = "0123456789" });

        result.GetProperty("query").GetProperty("npi").GetString().Should().Be("0123456789");
        var nineDigits = await SearchAsync(TestContext.Current.CancellationToken, new() { ["code"] = "45378", ["npi"] = "124507857" });
        nineDigits.GetProperty("query").GetProperty("npi").GetString().Should().Be("0124507857", "the files publish this NPI without its leading zero");
        Hint(result, "npi_check_digit").GetProperty("message").GetString().Should()
            .StartWith("NPI 0123456789 isn't a standard NPI: NPIs are 10 digits starting with 1 or 2.");
        Hint(result, "npi_not_listed").GetProperty("message").GetString().Should().Be("NPI 0123456789 isn't listed in this snapshot's files.");
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

    private static List<string?> Kinds(JsonElement result) =>
        result.GetProperty("hints").EnumerateArray().Select(h => h.GetProperty("kind").GetString()).ToList();

    private static JsonElement Hint(JsonElement result, string kind) =>
        result.GetProperty("hints").EnumerateArray().Single(h => h.GetProperty("kind").GetString() == kind);

    private static List<string?> LinkCodes(JsonElement hint) =>
        hint.GetProperty("links").EnumerateArray().Select(l => l.GetProperty("query").GetProperty("code").GetString()).ToList();
}
