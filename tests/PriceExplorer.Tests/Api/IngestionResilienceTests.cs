using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PriceExplorer.Api.Core.Ingestion;
using PriceExplorer.Tests.Support;

namespace PriceExplorer.Tests.Api;

public sealed class IngestionResilienceTests
{
    [Fact(Timeout = 60_000)]
    public async Task A_missing_rate_file_is_reported_and_the_other_file_still_loads()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = ExplorerApiFactory.With(http => http.Fail(FixtureHttpHandler.EssentialUrl, HttpStatusCode.NotFound));
        using var client = factory.CreateClient();

        var status = await factory.WaitForIngestionAsync(ct);
        var snapshot = (await client.GetFromJsonAsync<JsonElement>("/api/v1/snapshots", ct)).EnumerateArray().Single();

        status.Phase.Should().Be(IngestionPhase.Ready);
        status.DataAvailable.Should().BeTrue();
        status.Issues.Should().ContainSingle(issue => issue.Severity == "error" && issue.Message.Contains("Essential Plan"));
        snapshot.GetProperty("status").GetString().Should().Be("partial");

        var essential = snapshot.GetProperty("products").EnumerateArray().Single(p => p.GetProperty("productKey").GetString() == "essential-plan");
        essential.GetProperty("status").GetString().Should().Be("failed");
        essential.GetProperty("error").GetString().Should().Contain("404");
    }

    [Fact(Timeout = 60_000)]
    public async Task An_unreachable_index_is_reported_and_the_api_stays_up()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = ExplorerApiFactory.With(http => http.Fail(FixtureHttpHandler.IndexUrl, HttpStatusCode.ServiceUnavailable));
        using var client = factory.CreateClient();

        var status = await factory.WaitForIngestionAsync(ct);
        using var ready = await client.GetAsync("/health/ready", ct);
        using var live = await client.GetAsync("/health/live", ct);

        status.Phase.Should().Be(IngestionPhase.Failed);
        status.DataAvailable.Should().BeFalse();
        status.Issues.Should().ContainSingle(issue => issue.Message.Contains("Couldn't read index"));
        ready.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        live.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Http.Requests.Count(r => r.Url == FixtureHttpHandler.IndexUrl).Should().Be(2, "it retries up to MaxDownloadAttempts");
    }

    [Fact(Timeout = 90_000)]
    public async Task A_restart_with_unchanged_files_re_checks_them_and_reuses_the_saved_data()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("pte-restart-").FullName;
        try
        {
            DateTime? firstLoadedAt;
            await using (var first = ExplorerApiFactory.With(dataDirectory: directory))
            {
                using var client = first.CreateClient();
                await first.WaitForIngestionAsync(ct);
                firstLoadedAt = await AmbetterLoadedAtAsync(client, ct);
            }

            await using var second = ExplorerApiFactory.With(dataDirectory: directory);
            using var secondClient = second.CreateClient();
            var status = await second.WaitForIngestionAsync(ct);

            status.Phase.Should().Be(IngestionPhase.Ready);
            (await AmbetterLoadedAtAsync(secondClient, ct)).Should().Be(firstLoadedAt, "the file's bytes didn't change, so it isn't parsed again");
            second.Http.Requests.Where(r => r.Url == FixtureHttpHandler.AmbetterUrl).Should().OnlyContain(r => r.Conditional);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task Plan_groups_that_share_one_rate_file_all_keep_their_plans()
    {
        var ct = TestContext.Current.CancellationToken;
        var index = $$"""
            { "reporting_entity_name": "Test Payer", "reporting_entity_type": "health insurance issuer", "version": "2.0.0",
              "last_updated_on": "2026-09-01", "reporting_structure": [
                { "reporting_plans": [ { "plan_name": "SILVER PLAN A", "plan_id_type": "HIOS", "plan_id": "11111NY0010001", "plan_market_type": "individual" } ],
                  "in_network_files": [ { "description": "in-network file", "location": "{{FixtureHttpHandler.AmbetterUrl}}" } ] },
                { "reporting_plans": [ { "plan_name": "GOLD PLAN B", "plan_id_type": "HIOS", "plan_id": "22222NY0020002", "plan_market_type": "individual" } ],
                  "in_network_files": [ { "description": "in-network file", "location": "{{FixtureHttpHandler.AmbetterUrl}}" } ] } ] }
            """;
        await using var factory = ExplorerApiFactory.With(http => http.ServeJson(FixtureHttpHandler.IndexUrl, index));
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);

        var snapshot = (await client.GetFromJsonAsync<JsonElement>("/api/v1/snapshots", ct)).EnumerateArray().Single();
        var plans = await client.GetFromJsonAsync<JsonElement>("/api/v1/snapshots/1/plans", ct);

        var file = snapshot.GetProperty("products").EnumerateArray().Should().ContainSingle("the shared file is loaded once").Subject;
        file.GetProperty("planEntries").GetInt32().Should().Be(2);
        file.GetProperty("distinctPlanIds").GetInt32().Should().Be(2);
        plans.GetProperty("products").EnumerateArray().Single().GetProperty("plans").EnumerateArray()
            .Select(plan => plan.GetProperty("planId").GetString()).Should().BeEquivalentTo(["11111NY0010001", "22222NY0020002"]);
        factory.Http.Requests.Count(r => r.Url == FixtureHttpHandler.AmbetterUrl).Should().Be(1);
    }

    private static async Task<DateTime?> AmbetterLoadedAtAsync(HttpClient client, CancellationToken ct)
    {
        var snapshot = (await client.GetFromJsonAsync<JsonElement>("/api/v1/snapshots", ct)).EnumerateArray().Single();
        return snapshot.GetProperty("products").EnumerateArray()
            .Single(p => p.GetProperty("productKey").GetString() == "ambetter-from-fidelis-care")
            .GetProperty("loadedAt").GetDateTime();
    }
}
