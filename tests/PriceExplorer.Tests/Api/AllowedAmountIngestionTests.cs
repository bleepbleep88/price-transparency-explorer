using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Ingestion;
using PriceExplorer.Tests.Support;
using static PriceExplorer.Tests.Support.AllowedAmountJson;

namespace PriceExplorer.Tests.Api;

/// <summary>The index's out-of-network allowed-amount files load like rate files: same cache, transaction and rejects.</summary>
public sealed class AllowedAmountIngestionTests
{
    [Fact(Timeout = 60_000)]
    public async Task Every_allowed_amount_file_the_index_lists_is_saved_with_its_payments_and_providers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ExplorerApiFactory();
        _ = factory.Services;
        var status = await factory.WaitForIngestionAsync(ct);

        status.Phase.Should().Be(IngestionPhase.Ready);
        status.Issues.Should().BeEmpty();
        status.FilesTotal.Should().Be(2, "the progress counts rate files; allowed-amount files are extras");

        await using var connection = await factory.Services.GetRequiredService<DuckDbConnectionFactory>().OpenAsync(ct);
        var files = (await connection.QueryAsync<(string ProductKey, string Url, string Status, long PricesRaw, int RatesUnique, long Duplicates, string Sha256, string Etag, DateOnly LastUpdatedOn, DateTime HttpLastModified)>(
            """
            SELECT product_key, url, status, prices_raw, rates_unique, duplicates_removed, sha256, etag, file_last_updated_on, http_last_modified
            FROM source_files WHERE kind = 'allowed_amount' ORDER BY structure_index
            """)).ToList();

        files.Select(f => (f.ProductKey, f.Url, f.Status, f.PricesRaw, f.RatesUnique, f.Duplicates)).Should().Equal(
            ("ambetter-from-fidelis-care", FixtureHttpHandler.AmbetterAllowedUrl, "loaded", 39L, 27, 12L),
            ("essential-plan", FixtureHttpHandler.EssentialAllowedUrl, "loaded", 68L, 48, 20L));
        files.Should().AllSatisfy(f =>
        {
            f.Sha256.Should().HaveLength(64);
            f.Etag.Should().StartWith("\"");
            f.LastUpdatedOn.Should().Be(new DateOnly(2026, 8, 31));
            f.HttpLastModified.Should().Be(new DateTime(2026, 9, 4, 18, 23, 42));
        });

        (await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM oon_payments")).Should().Be(27 + 48);
        (await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM oon_items")).Should().Be(10);
        (await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM (SELECT DISTINCT payment_id, provider_idx FROM oon_providers)")).Should().Be(
            13 + 4 + 2 + 9 + 1 + 22 + 18 + 9, "one billed charge per provider entry, as counted by build_allowed_amount_fixtures.py");

        // An EIN published as 8 digits is keyed by its 9 digits, the way rate files publish it.
        var restored = await connection.QuerySingleAsync<(string TinValue, string Ein, string ProviderKey)>(
            "SELECT DISTINCT tin_value, ein, provider_key FROM oon_payments WHERE tin_value = '60646659'");
        restored.Should().Be(("60646659", "060646659", "060646659"));

        var snapshot = (await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/snapshots", ct)).EnumerateArray().Single();
        snapshot.GetProperty("products").GetArrayLength().Should().Be(2, "the snapshot list shows rate files");
    }

    [Fact(Timeout = 60_000)]
    public async Task A_missing_allowed_amount_file_is_a_warning_and_search_is_unaffected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = ExplorerApiFactory.With(http => http.Fail(FixtureHttpHandler.EssentialAllowedUrl, HttpStatusCode.NotFound));
        using var client = factory.CreateClient();

        var status = await factory.WaitForIngestionAsync(ct);
        var snapshot = (await client.GetFromJsonAsync<JsonElement>("/api/v1/snapshots", ct)).EnumerateArray().Single();
        var search = await client.GetFromJsonAsync<JsonElement>("/api/v1/rates?code=45378", ct);

        status.Phase.Should().Be(IngestionPhase.Ready);
        status.Issues.Should().ContainSingle().Which.Should().Match<IngestionIssue>(issue =>
            issue.Severity == "warning" && issue.Message.StartsWith("Essential Plan out-of-network amounts") && issue.Message.Contains("404")
            && issue.Source == FixtureHttpHandler.EssentialAllowedUrl);
        snapshot.GetProperty("status").GetString().Should().Be("ready", "a snapshot's status follows its rate files");
        search.GetProperty("groups").GetArrayLength().Should().BePositive();

        await using var connection = await factory.Services.GetRequiredService<DuckDbConnectionFactory>().OpenAsync(ct);
        var failed = await connection.QuerySingleAsync<(string Status, string Error)>(
            "SELECT status, error FROM source_files WHERE kind = 'allowed_amount' AND product_key = 'essential-plan'");
        failed.Status.Should().Be("failed");
        failed.Error.Should().Contain("404");
    }

    [Fact(Timeout = 90_000)]
    public async Task A_restart_re_checks_allowed_amount_files_and_does_not_parse_them_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("pte-oon-restart-").FullName;
        try
        {
            List<DateTime> firstLoadedAt;
            await using (var first = ExplorerApiFactory.With(dataDirectory: directory))
            {
                _ = first.Services;
                await first.WaitForIngestionAsync(ct);
                firstLoadedAt = await LoadedAtAsync(first, ct);
            }

            await using var second = ExplorerApiFactory.With(dataDirectory: directory);
            _ = second.Services;
            await second.WaitForIngestionAsync(ct);

            (await LoadedAtAsync(second, ct)).Should().Equal(firstLoadedAt, "the files' bytes didn't change, so they aren't parsed again");
            second.Http.Requests.Where(r => r.Url.Contains("allowed-amounts", StringComparison.Ordinal))
                .Should().HaveCount(2).And.OnlyContain(r => r.Conditional);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task The_index_may_list_allowed_amount_files_as_an_array_share_them_or_leave_them_out()
    {
        var ct = TestContext.Current.CancellationToken;
        var index = $$"""
            { "reporting_entity_name": "Test Payer", "reporting_entity_type": "health insurance issuer", "version": "2.0.0",
              "last_updated_on": "2026-08-31", "reporting_structure": [
                { "reporting_plans": [
                    { "plan_name": "Ambetter from Fidelis Care Bronze", "plan_id_type": "hios", "plan_id": "25303NY005", "plan_market_type": "individual" },
                    { "plan_name": "Ambetter from Fidelis Care Gold", "plan_id_type": "hios", "plan_id": "25303NY017", "plan_market_type": "individual" } ],
                  "in_network_files": [ { "description": "in-network file", "location": "{{FixtureHttpHandler.AmbetterUrl}}" } ],
                  "allowed_amount_file": [ { "description": "allowed-amounts file", "location": "{{FixtureHttpHandler.AmbetterAllowedUrl}}" }, null, "junk" ] },
                { "reporting_plans": [ { "plan_name": "Essential Plan 1", "plan_id_type": "hios", "plan_id": "25303EP000", "plan_market_type": "individual" } ],
                  "in_network_files": [ { "description": "in-network file", "location": "{{FixtureHttpHandler.EssentialUrl}}" } ],
                  "allowed_amount_file": { "description": "allowed-amounts file", "location": "{{FixtureHttpHandler.AmbetterAllowedUrl}}" } },
                { "reporting_plans": [ { "plan_name": "Gold Plan", "plan_id_type": "hios", "plan_id": "11111NY0010001", "plan_market_type": "individual" } ],
                  "in_network_files": [ { "description": "in-network file", "location": "{{FixtureHttpHandler.AmbetterUrl}}" } ],
                  "allowed_amount_file": "not an object" } ] }
            """;
        await using var factory = ExplorerApiFactory.With(http => http.ServeJson(FixtureHttpHandler.IndexUrl, index));
        _ = factory.Services;
        var status = await factory.WaitForIngestionAsync(ct);

        status.Phase.Should().Be(IngestionPhase.Ready);
        status.Issues.Should().BeEmpty();
        await using var connection = await factory.Services.GetRequiredService<DuckDbConnectionFactory>().OpenAsync(ct);
        var files = (await connection.QueryAsync<(string ProductKey, string Url)>(
            "SELECT product_key, url FROM source_files WHERE kind = 'allowed_amount'")).ToList();
        files.Should().ContainSingle("a file listed twice is loaded once, under the first structure's product")
            .Which.Should().Be(("ambetter-from-fidelis-care", FixtureHttpHandler.AmbetterAllowedUrl));
        factory.Http.Requests.Count(r => r.Url == FixtureHttpHandler.AmbetterAllowedUrl).Should().Be(1);
    }

    [Fact(Timeout = 60_000)]
    public async Task A_never_issued_prefix_gets_the_same_masked_key_as_in_the_rate_files()
    {
        var ct = TestContext.Current.CancellationToken;
        var allowed = File([
            Item("CPT", "99214", "OFFICE VISIT",
                Entry("""{ "type": "ein", "value": "071234567" }""", "[\"11\"]", "professional", Payment("150.00")),
                Entry("""{ "type": "ein", "value": "71234568" }""", "[\"11\"]", "professional", Payment("151.00"))),
        ]);
        await using var factory = ExplorerApiFactory.With(http =>
        {
            http.ServeJson(FixtureHttpHandler.EssentialUrl, TicFileBuilder.File(
                [TicFileBuilder.CountyClinicWholeTin],
                [TicFileBuilder.Item("CPT", "99214", "OFFICE VISIT", "[2]", TicFileBuilder.Price("150.29"))]));
            http.ServeJson(FixtureHttpHandler.EssentialAllowedUrl, allowed);
        });
        _ = factory.Services;
        await factory.WaitForIngestionAsync(ct);

        await using var connection = await factory.Services.GetRequiredService<DuckDbConnectionFactory>().OpenAsync(ct);
        var groupKey = await connection.ExecuteScalarAsync<string>("SELECT provider_key FROM provider_groups WHERE ein = '071234567'");
        var keys = (await connection.QueryAsync<(string Ein, bool Unverified, string ProviderKey)>(
            "SELECT ein, ein_unverified, provider_key FROM oon_payments WHERE file_id IN (SELECT file_id FROM source_files WHERE product_key = 'essential-plan') ORDER BY ein")).ToList();

        groupKey.Should().MatchRegex("^u[0-9a-f]{12}$");
        keys.Should().HaveCount(2);
        keys[0].Should().Be(("071234567", true, groupKey!), "one tax ID gets one key across both kinds of file");
        keys[1].Ein.Should().Be("071234568", "the lost leading zero is restored before the prefix is checked");
        keys[1].Unverified.Should().BeTrue();
        keys[1].ProviderKey.Should().MatchRegex("^u[0-9a-f]{12}$").And.NotBe(groupKey);
    }

    private static async Task<List<DateTime>> LoadedAtAsync(ExplorerApiFactory factory, CancellationToken ct)
    {
        await using var connection = await factory.Services.GetRequiredService<DuckDbConnectionFactory>().OpenAsync(ct);
        return (await connection.QueryAsync<DateTime>("SELECT loaded_at FROM source_files WHERE kind = 'allowed_amount' ORDER BY file_id")).ToList();
    }
}
