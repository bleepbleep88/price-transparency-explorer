using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Ingestion;
using PriceExplorer.Api.Core.Queries.Statistics;
using PriceExplorer.Tests.Support;

namespace PriceExplorer.Tests.Statistics;

/// <summary>When the saved statistics are built, rebuilt and kept, and what the loading status says meanwhile.</summary>
public sealed class StatisticsLifecycleTests
{
    private static readonly Dictionary<string, string?> LoadingOff = new() { ["Ingestion:Enabled"] = "false" };

    [Fact(Timeout = 120_000)]
    public async Task A_changed_rate_file_rebuilds_its_snapshots_statistics_and_an_unchanged_restart_keeps_them()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("pte-stats-").FullName;
        try
        {
            DateTimeOffset firstBuild;
            await using (var first = ExplorerApiFactory.With(dataDirectory: directory))
            {
                await first.WaitForIngestionAsync(ct);
                firstBuild = (await first.SendAsync(new GetGroupStatsQuery(Codes: ["29881"]), ct)).BuiltAt!.Value;
            }

            await using (var unchanged = ExplorerApiFactory.With(dataDirectory: directory))
            {
                await unchanged.WaitForIngestionAsync(ct);
                (await unchanged.SendAsync(new GetGroupStatsQuery(Codes: ["29881"]), ct)).BuiltAt
                    .Should().Be(firstBuild, "no rate file was saved again, so the saved statistics stay");
            }

            // The payer republishes the Ambetter file with one surgery center's knee arthroscopy rate changed.
            var republished = File.ReadAllText(TestFiles.Fixture("2026-08-31_fidelis-ex_in-network.json"))
                .Replace("\"negotiated_rate\": 613.33", "\"negotiated_rate\": 713.33", StringComparison.Ordinal);
            await using var changed = ExplorerApiFactory.With(http => http.ServeJson(FixtureHttpHandler.AmbetterUrl, republished), directory);
            using var client = changed.CreateClient();
            await changed.WaitForIngestionAsync(ct);

            var rebuilt = await changed.SendAsync(new GetGroupStatsQuery(Codes: ["29881"]), ct);
            var search = await client.GetFromJsonAsync<JsonElement>("/api/v1/rates?code=29881", ct);

            rebuilt.BuiltAt.Should().BeAfter(firstBuild);
            var group = rebuilt.Groups.Should().ContainSingle().Subject;
            group.Providers.Select(value => value.Value).Should().BeEquivalentTo([713.33m, 1183.01m]);
            search.GetProperty("groups")[0].GetProperty("listedRates").EnumerateArray().Select(rate => rate.GetDecimal())
                .Should().Equal(713.33m, 1183.01m);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task A_build_that_stops_midway_leaves_the_previous_statistics_in_place()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ExplorerApiFactory();
        await factory.WaitForIngestionAsync(ct);
        var before = await factory.SendAsync(new GetGroupStatsQuery(), ct);

        // Stops right after the old rows are deleted inside the build's transaction, as a shutdown would.
        using var stop = new CancellationTokenSource();
        var builder = factory.Services.GetRequiredService<SnapshotStatisticsBuilder>();
        var build = () => builder.BuildAsync(
            new StaleSnapshot(before.Snapshot.SnapshotId, before.Snapshot.IndexDate, "stopped"), (_, _) => stop.Cancel(), stop.Token);

        await build.Should().ThrowAsync<OperationCanceledException>();
        (await factory.SendAsync(new GetGroupStatsQuery(), ct)).Should().BeEquivalentTo(before);
        before.Groups.Should().NotBeEmpty();
    }

    [Fact(Timeout = 60_000)]
    public async Task While_statistics_build_the_loading_status_says_so()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ExplorerApiFactory();
        await factory.WaitForIngestionAsync(ct);
        var before = await factory.SendAsync(new GetGroupStatsQuery(), ct);
        var codes = before.Groups.Select(group => group.Code).Distinct().Count();

        // As if an earlier run stopped before building: the next build step picks the snapshot up again.
        await using (var connection = await factory.Services.GetRequiredService<DuckDbConnectionFactory>().OpenAsync(ct))
        {
            await connection.ExecuteAsync("DELETE FROM stats_builds");
        }

        await factory.Services.GetRequiredService<IngestionCoordinator>().BuildStatisticsAsync(ct);

        factory.Status.Current.Phase.Should().Be(IngestionPhase.BuildingStatistics);
        factory.Status.Current.Message.Should().Be($"Building statistics for snapshot 2026-08-31: {codes} of {codes} codes…");
        factory.Status.Current.DataAvailable.Should().BeTrue("search keeps answering while statistics build");
        (await factory.SendAsync(new GetGroupStatsQuery(), ct)).BuiltAt.Should().BeAfter(before.BuiltAt!.Value);
    }

    [Fact(Timeout = 90_000)]
    public async Task Statistics_a_stopped_run_never_built_are_built_from_saved_data_even_with_loading_off()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("pte-stats-off-").FullName;
        try
        {
            // The Ambetter file is saved, then the app stops while the Essential Plan file is still downloading.
            await using (var stopped = ExplorerApiFactory.With(http => http.Hold(FixtureHttpHandler.EssentialUrl), directory))
            {
                await foreach (var update in stopped.Status.WatchAsync(ct))
                {
                    if (update.DataAvailable)
                    {
                        break;
                    }
                }
            }

            await using var restarted = ExplorerApiFactory.With(dataDirectory: directory, settings: LoadingOff);
            var status = await restarted.WaitForIngestionAsync(ct);
            var saved = await restarted.SendAsync(new GetGroupStatsQuery(Codes: ["45378"]), ct);

            status.Phase.Should().Be(IngestionPhase.Ready);
            saved.BuiltAt.Should().NotBeNull();
            saved.Groups.Should().NotBeEmpty().And.OnlyContain(group => group.ProductKey == "ambetter-from-fidelis-care",
                "only the Ambetter file was saved before the stop");
            restarted.Http.Requests.Should().BeEmpty("loading is turned off");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
