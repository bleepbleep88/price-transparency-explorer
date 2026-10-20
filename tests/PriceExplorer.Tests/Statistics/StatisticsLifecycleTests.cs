using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using DuckDB.NET.Data;
using Microsoft.Extensions.DependencyInjection;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Ingestion;
using PriceExplorer.Api.Core.Models.Statistics;
using PriceExplorer.Api.Core.Queries.Statistics;
using PriceExplorer.Api.Core.Reference;
using PriceExplorer.Tests.Support;

namespace PriceExplorer.Tests.Statistics;

/// <summary>When the saved statistics are built, rebuilt and kept, and what readers and the loading status see meanwhile.</summary>
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
            GroupStatsResult first;
            await using (var initial = ExplorerApiFactory.With(dataDirectory: directory))
            {
                await initial.WaitForIngestionAsync(ct);
                first = await initial.SendAsync(new GetGroupStatsQuery(Codes: ["29881"]), ct);
            }

            await using (var unchanged = ExplorerApiFactory.With(dataDirectory: directory))
            {
                await unchanged.WaitForIngestionAsync(ct);
                (await unchanged.SendAsync(new GetGroupStatsQuery(Codes: ["29881"]), ct))
                    .Should().BeEquivalentTo(first, "no rate file was saved again, so the saved statistics stay, still current");
            }

            await using var changed = ExplorerApiFactory.With(http => http.ServeJson(FixtureHttpHandler.AmbetterUrl, RepublishedAmbetter()), directory);
            using var client = changed.CreateClient();
            await changed.WaitForIngestionAsync(ct);

            var rebuilt = await changed.SendAsync(new GetGroupStatsQuery(Codes: ["29881"]), ct);
            var search = await client.GetFromJsonAsync<JsonElement>("/api/v1/rates?code=29881", ct);

            first.Current.Should().BeTrue();
            rebuilt.Current.Should().BeTrue();
            rebuilt.BuiltAt.Should().BeAfter(first.BuiltAt!.Value);
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

    [Fact(Timeout = 120_000)]
    public async Task While_a_changed_rate_file_reloads_readers_are_told_the_saved_statistics_are_out_of_date()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("pte-stats-reload-").FullName;
        try
        {
            GroupStatsResult first;
            await using (var initial = ExplorerApiFactory.With(dataDirectory: directory))
            {
                await initial.WaitForIngestionAsync(ct);
                first = await initial.SendAsync(new GetGroupStatsQuery(Codes: ["29881"]), ct);
            }

            // The changed Ambetter file is saved again; the Essential Plan download is still running, so no rebuild yet.
            await using var reloading = ExplorerApiFactory.With(http =>
            {
                http.ServeJson(FixtureHttpHandler.AmbetterUrl, RepublishedAmbetter());
                http.Hold(FixtureHttpHandler.EssentialUrl);
            }, directory);
            await foreach (var status in reloading.Status.WatchAsync(ct))
            {
                if (status.FilesDone >= 1)
                {
                    break;
                }
            }

            var saved = await reloading.SendAsync(new GetGroupStatsQuery(Codes: ["29881"]), ct);

            first.Current.Should().BeTrue();
            saved.Current.Should().BeFalse("the Ambetter file was saved again after these statistics were built");
            saved.Should().BeEquivalentTo(first with { Current = false }, "the previous statistics stay readable until the rebuild");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory(Timeout = 120_000)]
    [InlineData("Ambetter Health from Fidelis Care")]   // a new product key
    [InlineData("AMBETTER FROM FIDELIS CARE")]          // the same key, a new label
    public async Task A_republished_index_that_renames_a_product_rebuilds_its_statistics(string renamed)
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("pte-stats-rename-").FullName;
        try
        {
            DateTimeOffset firstBuild;
            await using (var initial = ExplorerApiFactory.With(dataDirectory: directory))
            {
                await initial.WaitForIngestionAsync(ct);
                firstBuild = (await initial.SendAsync(new GetGroupStatsQuery(Codes: ["29881"]), ct)).BuiltAt!.Value;
            }

            // Same index URL and byte-identical rate files; only the plan names, and so the product, change.
            var index = File.ReadAllText(TestFiles.Fixture("2026-08-31_fidelis_index.json"))
                .Replace("\"plan_name\": \"Ambetter from Fidelis Care", $"\"plan_name\": \"{renamed}", StringComparison.Ordinal);
            await using var republished = ExplorerApiFactory.With(http => http.ServeJson(FixtureHttpHandler.IndexUrl, index), directory);
            await republished.WaitForIngestionAsync(ct);

            var saved = await SearchComparison.SavedEqualsSearchAsync(republished, "29881", ct);

            saved.BuiltAt.Should().BeAfter(firstBuild);
            saved.Groups.Should().OnlyContain(group => group.ProductLabel == renamed);
            republished.Http.Requests.Where(request => request.Url == FixtureHttpHandler.AmbetterUrl)
                .Should().OnlyContain(request => request.Conditional, "the rate file itself wasn't downloaded again");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task A_build_that_stops_between_batches_leaves_the_previous_statistics_and_shows_nothing_half_written()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ExplorerApiFactory();
        await factory.WaitForIngestionAsync(ct);

        // Mark the saved rows, so rows a rebuild writes can't pass for them.
        await ExecuteAsync(factory, "UPDATE group_stats SET max = max + 1", ct);
        await ExecuteAsync(factory, "UPDATE provider_values SET high = high + 1", ct);
        var before = await factory.SendAsync(new GetGroupStatsQuery(), ct);
        var state = await StateAsync(factory, before.Snapshot.SnapshotId, ct);

        // Five rates per batch, so the fixtures build in several batches. After the first batch's rows are appended
        // inside the build's transaction, the build waits while the test reads, then stops as a shutdown would.
        var builder = new SnapshotStatisticsBuilder(
            factory.Services.GetRequiredService<DuckDbConnectionFactory>(),
            factory.Services.GetRequiredService<ReferenceCatalog>(),
            TimeProvider.System) { BatchRates = 5 };
        using var appended = new SemaphoreSlim(0);
        using var resume = new SemaphoreSlim(0);
        using var stop = new CancellationTokenSource();
        var build = Task.Run(() => builder.BuildAsync(state, (done, total) =>
        {
            if (done > 0 && done < total && !stop.IsCancellationRequested)
            {
                appended.Release();
                resume.Wait(ct);
                stop.Cancel();
            }
        }, stop.Token), ct);

        await appended.WaitAsync(ct);
        var midway = await factory.SendAsync(new GetGroupStatsQuery(), ct);
        resume.Release();
        var stopped = () => build;

        await stopped.Should().ThrowAsync<OperationCanceledException>();
        midway.Should().BeEquivalentTo(before, "rows a build has appended stay invisible until it commits");
        (await factory.SendAsync(new GetGroupStatsQuery(), ct)).Should().BeEquivalentTo(before, "a stopped build leaves the previous statistics");
        before.Groups.Should().NotBeEmpty();
    }

    [Fact(Timeout = 60_000)]
    public async Task While_statistics_build_the_status_says_so_search_answers_and_readers_get_the_previous_statistics()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ExplorerApiFactory();
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);

        // As if the app was rebuilt since: the saved statistics stay readable but are no longer current.
        await ExecuteAsync(factory, "UPDATE stats_builds SET code_version = 'an-older-app'", ct);
        var before = await factory.SendAsync(new GetGroupStatsQuery(), ct);
        var codes = before.Groups.Select(group => group.Code).Distinct().Count();

        // The build reads the clock once, to stamp built_at, after every batch is appended and reported and before it
        // commits. The clock holds it there until the test has looked.
        using var paused = new SemaphoreSlim(0);
        using var resume = new SemaphoreSlim(0);
        var builder = ActivatorUtilities.CreateInstance<SnapshotStatisticsBuilder>(factory.Services, (TimeProvider)new PausingClock(paused, resume, ct));
        var coordinator = ActivatorUtilities.CreateInstance<IngestionCoordinator>(factory.Services, builder);
        var build = Task.Run(() => coordinator.BuildStatisticsAsync(ct), ct);
        await paused.WaitAsync(ct);

        IngestionStatusSnapshot during;
        await using (var stream = factory.Status.WatchAsync(ct).GetAsyncEnumerator(ct))
        {
            await stream.MoveNextAsync();
            during = stream.Current;
        }

        using var search = await client.GetAsync("/api/v1/rates?code=45378", ct);
        var read = await factory.SendAsync(new GetGroupStatsQuery(), ct);
        resume.Release();
        await build;
        var after = await factory.SendAsync(new GetGroupStatsQuery(), ct);

        during.Phase.Should().Be(IngestionPhase.BuildingStatistics);
        during.Message.Should().Be($"Building statistics for snapshot 2026-08-31: {codes} of {codes} codes…");
        search.StatusCode.Should().Be(HttpStatusCode.OK, "search keeps answering while statistics build");
        before.Current.Should().BeFalse();
        read.Should().BeEquivalentTo(before, "readers get the previous statistics, marked out of date, until the build commits");
        after.Current.Should().BeTrue();
        after.BuiltAt.Should().BeAfter(before.BuiltAt!.Value);
    }

    [Theory(Timeout = 90_000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Statistics_a_stopped_run_never_built_are_built_from_saved_data_even_with_loading_off(bool crashed)
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

            if (crashed)
            {
                // A crash skips settling the snapshot: it stays 'loading', and search still serves it.
                await using var connection = new DuckDBConnection($"Data Source={Path.Combine(directory, "explorer.duckdb")}");
                await connection.OpenAsync(ct);
                await connection.ExecuteAsync("UPDATE snapshots SET status = 'loading', error = NULL");
            }

            await using var restarted = ExplorerApiFactory.With(dataDirectory: directory, settings: LoadingOff);
            var status = await restarted.WaitForIngestionAsync(ct);
            var saved = await restarted.SendAsync(new GetGroupStatsQuery(Codes: ["45378"]), ct);

            status.Phase.Should().Be(IngestionPhase.Ready);
            saved.BuiltAt.Should().NotBeNull();
            saved.Current.Should().BeTrue();
            saved.Groups.Should().NotBeEmpty().And.OnlyContain(group => group.ProductKey == "ambetter-from-fidelis-care",
                "only the Ambetter file was saved before the stop");
            restarted.Http.Requests.Should().BeEmpty("loading is turned off");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The Ambetter file as the payer republishes it, with one surgery center's knee arthroscopy rate changed.</summary>
    private static string RepublishedAmbetter() =>
        File.ReadAllText(TestFiles.Fixture("2026-08-31_fidelis-ex_in-network.json"))
            .Replace("\"negotiated_rate\": 613.33", "\"negotiated_rate\": 713.33", StringComparison.Ordinal);

    private static async Task ExecuteAsync(ExplorerApiFactory factory, string sql, CancellationToken ct)
    {
        await using var connection = await factory.Services.GetRequiredService<DuckDbConnectionFactory>().OpenAsync(ct);
        await connection.ExecuteAsync(sql);
    }

    private static async Task<StatisticsState> StateAsync(ExplorerApiFactory factory, int snapshotId, CancellationToken ct)
    {
        await using var connection = await factory.Services.GetRequiredService<DuckDbConnectionFactory>().OpenAsync(ct);
        return (await SavedStatistics.StateAsync(connection, snapshotId)).Single();
    }

    /// <summary>A clock whose reads wait until the test lets them go.</summary>
    private sealed class PausingClock(SemaphoreSlim paused, SemaphoreSlim resume, CancellationToken cancellationToken) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            paused.Release();
            resume.Wait(cancellationToken);
            return base.GetUtcNow();
        }
    }
}
