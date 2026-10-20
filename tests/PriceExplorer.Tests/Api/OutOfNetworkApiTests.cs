using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PriceExplorer.Tests.Support;
using static PriceExplorer.Tests.Support.AllowedAmountJson;

namespace PriceExplorer.Tests.Api;

/// <summary>Expected numbers come from build_allowed_amount_fixtures.py, which recounts the fixtures independently.</summary>
public sealed class OutOfNetworkApiTests(ExplorerApiFactory factory) : IClassFixture<ExplorerApiFactory>
{
    [Fact(Timeout = 60_000)]
    public async Task A_code_with_out_of_network_payments_is_summarized_per_product_and_fee_type()
    {
        var result = await GetAsync("/api/v1/out-of-network?code=0450", TestContext.Current.CancellationToken);

        result.GetProperty("snapshot").GetProperty("isLatest").GetBoolean().Should().BeTrue();
        result.GetProperty("files").EnumerateArray()
            .Select(f => (f.GetProperty("productKey").GetString(), f.GetProperty("status").GetString(), f.GetProperty("url").GetString()))
            .Should().Equal(
                ("ambetter-from-fidelis-care", "loaded", FixtureHttpHandler.AmbetterAllowedUrl),
                ("essential-plan", "loaded", FixtureHttpHandler.EssentialAllowedUrl));

        var groups = result.GetProperty("groups").EnumerateArray().ToList();
        groups.Select(g => g.GetProperty("key").GetString()).Should().Equal(
            "ambetter-from-fidelis-care|RC|0450|facility|", "essential-plan|RC|0450|facility|");

        var ambetter = groups[0];
        ambetter.GetProperty("feeTypeLabel").GetString().Should().Be("Facility fee");
        ambetter.GetProperty("perUnitCode").GetBoolean().Should().BeFalse();
        ambetter.GetProperty("places").EnumerateArray().Select(p => p.GetProperty("name").GetString()).Should().Equal("Emergency room (hospital)");
        (ambetter.GetProperty("payments").GetInt32(), ambetter.GetProperty("providers").GetInt32(), ambetter.GetProperty("npis").GetInt32())
            .Should().Be((12, 3, 4), "the 24 published payments are the same 12 under two item names");
        Amounts(ambetter.GetProperty("allowed")).Should().Be((12, 23.38m, 1185.02m, 18803.47m, 0), "1,185.015 rounds half away from zero");
        Amounts(ambetter.GetProperty("billed")).Should().Be((13, 434.92m, 2233m, 217627m, 0), "one payment lists two providers");
        ambetter.GetProperty("billedToAllowed").GetDecimal().Should().Be(3.28m);

        var essential = groups[1];
        (essential.GetProperty("payments").GetInt32(), essential.GetProperty("providers").GetInt32(), essential.GetProperty("npis").GetInt32())
            .Should().Be((20, 5, 7));
        Amounts(essential.GetProperty("allowed")).Should().Be((20, 60.12m, 1375.18m, 187538.55m, 0));
        Amounts(essential.GetProperty("billed")).Should().Be((22, 551.92m, 5110m, 1733257m, 0));
        essential.GetProperty("billedToAllowed").GetDecimal().Should().Be(4.81m);
    }

    [Fact(Timeout = 60_000)]
    public async Task Under_five_amounts_they_are_listed_instead_of_summarized()
    {
        var groups = (await GetAsync("/api/v1/out-of-network?code=74177", TestContext.Current.CancellationToken)).GetProperty("groups").EnumerateArray().ToList();

        groups.Select(g => g.GetProperty("key").GetString()).Should().Equal(
            "ambetter-from-fidelis-care|CPT|74177|professional|", "essential-plan|CPT|74177|professional|");

        var ambetter = groups[0];
        ambetter.GetProperty("feeTypeLabel").GetString().Should().Be("Professional (clinician) fee");
        ambetter.GetProperty("places").EnumerateArray().Select(p => p.GetProperty("pos").GetString()).Should().Equal("19", "21", "22", "23");
        var allowed = ambetter.GetProperty("allowed");
        allowed.GetProperty("median").ValueKind.Should().Be(JsonValueKind.Null, "a midpoint of 4 amounts would read as a price nobody was paid");
        allowed.GetProperty("listed").EnumerateArray().Select(a => a.GetDecimal()).Should().Equal(217.92m, 544.80m, 762.72m, 8373.66m);
        ambetter.GetProperty("billed").GetProperty("listed").EnumerateArray().Select(a => a.GetDecimal()).Should().Equal(3892m, 9730m, 13622m, 134647.88m);
        ambetter.GetProperty("billedToAllowed").ValueKind.Should().Be(JsonValueKind.Null, "4 pairs are too few for a median");

        var essential = groups[1];
        Amounts(essential.GetProperty("allowed")).Should().Be((18, 91.25m, 349.79m, 64150.47m, 0));
        Amounts(essential.GetProperty("billed")).Should().Be((18, 246m, 2640m, 1452463.76m, 0));
        essential.GetProperty("billedToAllowed").GetDecimal().Should().Be(4.70m);
    }

    [Theory(Timeout = 60_000)]
    [InlineData("450")]
    [InlineData("0450")]
    [InlineData("RC 450")]
    [InlineData("rc0450")]
    [InlineData("450&codeType=RC")]
    public async Task The_code_is_read_the_way_search_reads_it(string code)
    {
        var url = $"/api/v1/out-of-network?code={code.Replace(" ", "%20", StringComparison.Ordinal)}";
        var groups = (await GetAsync(url, TestContext.Current.CancellationToken)).GetProperty("groups");

        groups.EnumerateArray().Select(g => g.GetProperty("key").GetString()).Should().Equal(
            "ambetter-from-fidelis-care|RC|0450|facility|", "essential-plan|RC|0450|facility|");
    }

    [Fact(Timeout = 60_000)]
    public async Task Published_without_leading_zeros_and_priced_per_unit_in_network()
    {
        var anesthesia = (await GetAsync("/api/v1/out-of-network?code=812", TestContext.Current.CancellationToken)).GetProperty("groups").EnumerateArray().ToList();
        var lens = (await GetAsync("/api/v1/out-of-network?code=00142", TestContext.Current.CancellationToken)).GetProperty("groups").EnumerateArray().ToList();
        var otherType = (await GetAsync("/api/v1/out-of-network?code=450&codeType=CPT", TestContext.Current.CancellationToken)).GetProperty("groups");

        anesthesia.Select(g => g.GetProperty("code").GetString()).Should().Equal("00812", "00812");
        anesthesia.Should().AllSatisfy(g => g.GetProperty("perUnitCode").GetBoolean().Should().BeTrue("in-network anesthesia rates are per unit"));
        Amounts(anesthesia[1].GetProperty("allowed")).Should().Be((9, 60m, 1358m, 28370m, 0));
        anesthesia[1].GetProperty("billedToAllowed").GetDecimal().Should().Be(33.33m);
        lens.Should().ContainSingle().Which.GetProperty("productKey").GetString().Should().Be("essential-plan");
        otherType.GetArrayLength().Should().Be(0, "CPT 00450 has no out-of-network payments");
    }

    [Fact(Timeout = 60_000)]
    public async Task A_code_without_out_of_network_payments_has_no_groups_but_lists_the_files_checked()
    {
        var result = await GetAsync("/api/v1/out-of-network?code=99213", TestContext.Current.CancellationToken);

        result.GetProperty("groups").GetArrayLength().Should().Be(0);
        result.GetProperty("files").GetArrayLength().Should().Be(2);
        result.GetProperty("query").GetProperty("code").GetString().Should().Be("99213");
    }

    [Theory(Timeout = 60_000)]
    [InlineData("", "code", "Enter a billing code, for example 45378 (colonoscopy) or 0490 (ambulatory surgery).")]
    [InlineData("?code=45378-99", "code", "A billing code is up to 8 letters and digits, for example 45378, J0585 or 0490.")]
    [InlineData("?code=450&codeType=XYZ", "codeType", null)]
    [InlineData("?code=450&snapshot=0", "snapshot", "Snapshot id must be a positive number.")]
    public async Task Bad_input_gets_the_search_messages(string query, string field, string? message)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);

        using var response = await client.GetAsync($"/api/v1/out-of-network{query}", ct);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        problem.GetProperty("code").GetString().Should().Be("validation_failed");
        var errors = problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString()).ToList();
        if (message is not null)
        {
            errors.Should().Contain(message);
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task An_unknown_snapshot_is_not_found()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);

        using var response = await client.GetAsync("/api/v1/out-of-network?code=0450&snapshot=99", ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be("snapshot_not_found");
    }

    [Fact(Timeout = 60_000)]
    public async Task While_no_rate_data_is_saved_it_answers_503_with_retry_after()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var loading = ExplorerApiFactory.With(http => http.Hold(FixtureHttpHandler.AmbetterUrl));
        using var client = loading.CreateClient();
        await loading.Http.WhenRequested(FixtureHttpHandler.AmbetterUrl).WaitAsync(ct);

        using var response = await client.GetAsync("/api/v1/out-of-network?code=0450", ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.Should().NotBeNull();
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be("index_loading");
    }

    [Fact(Timeout = 60_000)]
    public async Task Tax_ids_are_counted_never_shown_and_a_masked_one_counts_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var allowed = File([
            Item("CPT", "99214", "OFFICE VISIT",
                Entry("""{ "type": "ein", "value": "071234567" }""", "[\"11\"]", "professional", Payment("150.00"), Payment("160.00")),
                Entry("""{ "type": "ein", "value": "71234567" }""", "[\"11\"]", "professional", Payment("170.00")),
                Entry("""{ "type": "ein", "value": "131740114" }""", "[\"11\"]", "professional", Payment("180.00"))),
        ]);
        await using var custom = ExplorerApiFactory.With(http => http.ServeJson(FixtureHttpHandler.EssentialAllowedUrl, allowed));
        using var client = custom.CreateClient();
        await custom.WaitForIngestionAsync(ct);

        var body = await client.GetStringAsync("/api/v1/out-of-network?code=99214", ct);
        var group = JsonDocument.Parse(body).RootElement.GetProperty("groups").EnumerateArray().Single();

        group.GetProperty("payments").GetInt32().Should().Be(4);
        group.GetProperty("providers").GetInt32().Should().Be(2, "07-1234567 is one tax ID however it was written");
        body.Should().NotContain("1234567").And.NotContain("131740114");
    }

    [Fact(Timeout = 60_000)]
    public async Task The_openapi_document_describes_both_endpoints()
    {
        var document = await GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var paths = document.GetProperty("paths");

        var outOfNetwork = paths.GetProperty("/api/v1/out-of-network").GetProperty("get");
        outOfNetwork.GetProperty("parameters").EnumerateArray().Select(p => (p.GetProperty("name").GetString(), p.TryGetProperty("required", out var r) && r.GetBoolean()))
            .Should().BeEquivalentTo([("code", true), ("codeType", false), ("snapshot", false)]);
        outOfNetwork.GetProperty("responses").EnumerateObject().Select(r => r.Name).Should().BeEquivalentTo(["200", "400", "404", "503"]);
        outOfNetwork.GetProperty("responses").GetProperty("400").GetProperty("content").EnumerateObject().Select(c => c.Name)
            .Should().Equal("application/problem+json");
        paths.GetProperty("/api/v1/sources").GetProperty("get").GetProperty("responses").GetProperty("200").GetProperty("content")
            .EnumerateObject().Select(c => c.Name).Should().Equal("application/json");
    }

    private async Task<JsonElement> GetAsync(string url, CancellationToken ct)
    {
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);
        return await client.GetFromJsonAsync<JsonElement>(url, ct);
    }

    /// <summary>(count, min, median, max, listed amounts) of an amount summary.</summary>
    private static (int, decimal, decimal?, decimal, int) Amounts(JsonElement summary) => (
        summary.GetProperty("count").GetInt32(),
        summary.GetProperty("min").GetDecimal(),
        summary.GetProperty("median").ValueKind == JsonValueKind.Null ? null : summary.GetProperty("median").GetDecimal(),
        summary.GetProperty("max").GetDecimal(),
        summary.GetProperty("listed").GetArrayLength());
}
