using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using PriceExplorer.Tests.Support;
using static PriceExplorer.Tests.Support.AllowedAmountJson;

namespace PriceExplorer.Tests.Api;

public sealed class SourcesApiTests(ExplorerApiFactory factory) : IClassFixture<ExplorerApiFactory>
{
    [Fact(Timeout = 60_000)]
    public async Task Every_file_is_listed_with_its_url_dates_checksums_counts_and_timing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);

        var sources = await client.GetFromJsonAsync<JsonElement>("/api/v1/sources", ct);

        var snapshot = sources.EnumerateArray().Should().ContainSingle().Subject;
        snapshot.GetProperty("indexDate").GetString().Should().Be("2026-08-31");
        snapshot.GetProperty("indexUrl").GetString().Should().Be(FixtureHttpHandler.IndexUrl);
        snapshot.GetProperty("indexSha256").GetString().Should().Be(Sha256Of("2026-08-31_fidelis_index.json"));
        snapshot.GetProperty("schemaVersion").GetString().Should().Be("2.0.0");
        snapshot.GetProperty("status").GetString().Should().Be("ready");
        snapshot.GetProperty("isLatest").GetBoolean().Should().BeTrue();

        var files = snapshot.GetProperty("files").EnumerateArray().ToList();
        files.Select(f => (f.GetProperty("kind").GetString(), f.GetProperty("productKey").GetString())).Should().Equal(
            ("in_network", "ambetter-from-fidelis-care"),
            ("in_network", "essential-plan"),
            ("allowed_amount", "ambetter-from-fidelis-care"),
            ("allowed_amount", "essential-plan"));

        (string Url, string Fixture)[] expected =
        [
            (FixtureHttpHandler.AmbetterUrl, "2026-08-31_fidelis-ex_in-network.json"),
            (FixtureHttpHandler.EssentialUrl, "2026-08-31_fidelis-es_in-network.json"),
            (FixtureHttpHandler.AmbetterAllowedUrl, "2026-08-31_fidelis-ex_allowed-amounts.json"),
            (FixtureHttpHandler.EssentialAllowedUrl, "2026-08-31_fidelis-es_allowed-amounts.json"),
        ];
        foreach (var (file, (url, fixture)) in files.Zip(expected))
        {
            file.GetProperty("url").GetString().Should().Be(url);
            file.GetProperty("status").GetString().Should().Be("loaded");
            file.GetProperty("sha256").GetString().Should().Be(Sha256Of(fixture));
            file.GetProperty("eTag").GetString().Should().Be($"\"{Sha256Of(fixture)[..16].ToUpperInvariant()}\"", "the payer's ETag, as served");
            file.GetProperty("contentLength").GetInt64().Should().Be(new FileInfo(TestFiles.Fixture(fixture)).Length);
            file.GetProperty("fileSchemaVersion").GetString().Should().Be("2.0.0");
            file.GetProperty("rejects").GetArrayLength().Should().Be(0);

            var dates = file.GetProperty("dates");
            dates.GetProperty("indexDate").GetString().Should().Be("2026-08-31");
            dates.GetProperty("httpLastModified").GetString().Should().Be("2026-09-04T18:23:42+00:00");

            var timing = file.GetProperty("timing");
            timing.GetProperty("downloadMs").GetInt32().Should().BeGreaterThanOrEqualTo(0);
            timing.GetProperty("parseMs").GetInt32().Should().BeGreaterThanOrEqualTo(0);
            timing.GetProperty("loadedAt").GetString().Should().EndWith("+00:00");
        }

        files[0].GetProperty("dates").GetProperty("fileLastUpdatedOn").GetString().Should().Be("2026-09-02");
        Counts(files[0]).Should().BeEquivalentTo(new Dictionary<string, long?>
        {
            ["items"] = 7, ["published"] = 27, ["unique"] = 23, ["duplicatesRemoved"] = 4, ["rejected"] = 0,
            ["providerGroups"] = 768, ["distinctNpis"] = 3169, ["distinctEins"] = 764, ["invalidNpis"] = 1, ["unverifiedEins"] = 67,
            ["wholeTaxIdGroups"] = 2, ["danglingReferences"] = 0, ["codesWithZerosRestored"] = 3, ["ratesFromNotes"] = 7,
            ["einsWithZeroRestored"] = null,
        });
        Counts(files[1])["unverifiedEins"].Should().Be(7);
        Counts(files[1])["codesWithZerosRestored"].Should().Be(1, "only RC 490 is published without its zero");

        files[3].GetProperty("dates").GetProperty("fileLastUpdatedOn").GetString().Should().Be("2026-08-31");
        Counts(files[3]).Should().BeEquivalentTo(new Dictionary<string, long?>
        {
            ["items"] = 5, ["published"] = 68, ["unique"] = 48, ["duplicatesRemoved"] = 20, ["rejected"] = 0,
            ["providerGroups"] = 24, ["distinctNpis"] = 24, ["distinctEins"] = 19, ["invalidNpis"] = 0, ["unverifiedEins"] = 0,
            ["wholeTaxIdGroups"] = null, ["danglingReferences"] = null, ["codesWithZerosRestored"] = 3, ["ratesFromNotes"] = null,
            ["einsWithZeroRestored"] = 3,
        });
    }

    [Fact(Timeout = 60_000)]
    public async Task Rejects_are_listed_by_reason_with_plain_words_and_tax_ids_masked()
    {
        var ct = TestContext.Current.CancellationToken;
        var allowed = File([
            Item("CPT", "74177", "Item 1",
                Entry(payments: [Payment("\"N/A\""), Payment("\"N/A\""), "null", Payment("217.92")]),
                Entry("""{ "type": "ein", "value": "12-3456" }""", payments: [Payment("300.00")]),
                Entry("""{ "type": "ein", "value": "071234567" }""", payments: [Payment("301.00")])),
        ]);
        await using var custom = ExplorerApiFactory.With(http => http.ServeJson(FixtureHttpHandler.EssentialAllowedUrl, allowed));
        using var client = custom.CreateClient();
        await custom.WaitForIngestionAsync(ct);

        var body = await client.GetStringAsync("/api/v1/sources", ct);
        var file = JsonDocument.Parse(body).RootElement.EnumerateArray().Single().GetProperty("files").EnumerateArray()
            .Single(f => f.GetProperty("kind").GetString() == "allowed_amount" && f.GetProperty("productKey").GetString() == "essential-plan");

        var rejects = file.GetProperty("rejects").EnumerateArray()
            .Select(r => (r.GetProperty("reason").GetString(), r.GetProperty("count").GetInt64(), r.GetProperty("example").GetString(), r.GetProperty("description").GetString()))
            .ToList();
        rejects.Should().Equal(
            ("allowed_amount_not_a_number", 2L, "out_of_network[0].allowed_amounts[0].payments[0]", "An allowed amount that isn't a number. The payment is skipped."),
            ("ein_not_nine_digits", 1L, "••-3456", "A tax ID marked as an EIN that isn't 9 digits. Kept as published, not read as an EIN."),
            ("null_payment", 1L, "out_of_network[0].allowed_amounts[0].payments[2]", "An empty (null) payment. Skipped."));
        Counts(file)["rejected"].Should().Be(3);
        Counts(file)["unverifiedEins"].Should().Be(1);
        body.Should().NotContain("12-3456").And.NotContain("071234567").And.NotContain("1234567", "no tax ID value is shown in full");
    }

    [Fact(Timeout = 60_000)]
    public async Task A_file_not_loaded_yet_or_never_loaded_has_no_counts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var custom = ExplorerApiFactory.With(http => http.Fail(FixtureHttpHandler.EssentialAllowedUrl, HttpStatusCode.NotFound));
        using var client = custom.CreateClient();
        await custom.WaitForIngestionAsync(ct);

        var files = (await client.GetFromJsonAsync<JsonElement>("/api/v1/sources", ct)).EnumerateArray().Single().GetProperty("files")
            .EnumerateArray().ToList();
        var failed = files.Single(f => f.GetProperty("kind").GetString() == "allowed_amount" && f.GetProperty("productKey").GetString() == "essential-plan");

        failed.GetProperty("status").GetString().Should().Be("failed");
        failed.GetProperty("error").GetString().Should().Contain("404");
        failed.GetProperty("counts").ValueKind.Should().Be(JsonValueKind.Null);
        failed.GetProperty("sha256").ValueKind.Should().Be(JsonValueKind.Null);
        files.Count(f => f.GetProperty("status").GetString() == "loaded").Should().Be(3);
    }

    [Fact(Timeout = 60_000)]
    public async Task While_loading_the_list_shows_what_is_saved_so_far()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var custom = ExplorerApiFactory.With(http => http.Hold(FixtureHttpHandler.EssentialUrl));
        using var client = custom.CreateClient();

        // The second rate file's row is saved just before its download starts, and the download never finishes.
        await custom.Http.WhenRequested(FixtureHttpHandler.EssentialUrl).WaitAsync(ct);

        var snapshot = (await client.GetFromJsonAsync<JsonElement>("/api/v1/sources", ct)).EnumerateArray().Single();

        snapshot.GetProperty("status").GetString().Should().Be("loading");
        snapshot.GetProperty("files").EnumerateArray()
            .Select(f => (f.GetProperty("productKey").GetString(), f.GetProperty("status").GetString(), f.GetProperty("counts").ValueKind == JsonValueKind.Null))
            .Should().Equal(("ambetter-from-fidelis-care", "loaded", false), ("essential-plan", "pending", true));
    }

    [Fact(Timeout = 60_000)]
    public async Task With_nothing_saved_the_list_is_empty()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var custom = ExplorerApiFactory.With(settings: new Dictionary<string, string?> { ["Ingestion:Enabled"] = "false" });
        using var client = custom.CreateClient();
        await custom.WaitForIngestionAsync(ct);

        using var response = await client.GetAsync("/api/v1/sources", ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetArrayLength().Should().Be(0);
    }

    private static Dictionary<string, long?> Counts(JsonElement file) =>
        file.GetProperty("counts").EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.ValueKind == JsonValueKind.Null ? (long?)null : property.Value.GetInt64());

    private static string Sha256Of(string fixture) =>
        Convert.ToHexStringLower(SHA256.HashData(System.IO.File.ReadAllBytes(TestFiles.Fixture(fixture))));
}
