using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Ingestion;
using PriceExplorer.Tests.Support;
using static PriceExplorer.Tests.Support.HistoryFiles;

namespace PriceExplorer.Tests.History;

/// <summary>History while snapshots load, while saved statistics are out of date, and with a single snapshot.</summary>
public sealed class HistoryLifecycleTests
{
    [Fact(Timeout = 60_000)]
    public async Task The_newest_snapshot_loads_first_so_it_is_the_one_shown_while_older_ones_download()
    {
        var ct = TestContext.Current.CancellationToken;

        // July's files never arrive. Loaded oldest first, nothing would be saved at all.
        await using var factory = Factory(http =>
        {
            http.Hold(AmbetterUrl(July));
            http.Hold(EssentialUrl(July));
        });
        using var client = factory.CreateClient();
        var waiting = await WaitForDownloadOfAsync(factory, July, ct);

        var snapshots = (await client.GetFromJsonAsync<JsonElement>("/api/v1/snapshots", ct)).EnumerateArray()
            .ToDictionary(snapshot => snapshot.GetProperty("indexDate").GetString()!);
        var search = await client.GetFromJsonAsync<JsonElement>("/api/v1/rates?code=45378", ct);

        waiting.FilesDone.Should().Be(4, "September's and August's files were saved first");
        snapshots[September].GetProperty("isLatest").GetBoolean().Should().BeTrue();
        snapshots[September].GetProperty("status").GetString().Should().Be("ready");
        snapshots[August].GetProperty("status").GetString().Should().Be("ready");
        snapshots[July].GetProperty("status").GetString().Should().Be("loading");
        search.GetProperty("snapshot").GetProperty("indexDate").GetString().Should().Be(September);
    }

    [Fact(Timeout = 60_000)]
    public async Task While_a_snapshots_saved_statistics_are_out_of_date_changes_say_so_and_give_none_of_its_rate_ids()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Factory();
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);

        // As if the app was rebuilt since September's statistics were saved: they stay readable, but not as current.
        await using (var connection = await factory.Services.GetRequiredService<DuckDbConnectionFactory>().OpenAsync(ct))
        {
            await connection.ExecuteAsync(
                "UPDATE stats_builds SET code_version = 'an-older-app' WHERE snapshot_id = (SELECT snapshot_id FROM snapshots WHERE index_date = DATE '2026-09-28')");
        }

        var result = await client.GetFromJsonAsync<JsonElement>("/api/v1/changes", ct);
        var rows = result.GetProperty("rows").GetProperty("items").EnumerateArray().ToList();

        result.GetProperty("current").GetBoolean().Should().BeFalse();
        result.GetProperty("to").GetProperty("statisticsCurrent").GetBoolean().Should().BeFalse();
        result.GetProperty("to").GetProperty("statisticsBuiltAt").ValueKind.Should().Be(JsonValueKind.String, "the last build is still readable");
        result.GetProperty("from").GetProperty("statisticsCurrent").GetBoolean().Should().BeTrue();
        rows.Should().HaveCount(9);
        rows.Should().OnlyContain(row => row.GetProperty("toRateId").ValueKind == JsonValueKind.Null);
        rows.Where(row => row.GetProperty("kind").GetString() != "new")
            .Should().OnlyContain(row => row.GetProperty("fromRateId").ValueKind == JsonValueKind.Number);
    }

    [Fact(Timeout = 60_000)]
    public async Task With_one_snapshot_there_is_nothing_to_compare_and_each_trend_has_one_point()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ExplorerApiFactory();
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);

        using var changes = await client.GetAsync("/api/v1/changes", ct);
        var trend = await client.GetFromJsonAsync<JsonElement>("/api/v1/rates/trend?code=45378", ct);
        var snapshots = await client.GetFromJsonAsync<JsonElement>("/api/v1/snapshots", ct);

        changes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await changes.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be("no_earlier_snapshot");
        trend.GetProperty("snapshots").GetArrayLength().Should().Be(1);
        trend.GetProperty("groups").EnumerateArray().Should().NotBeEmpty()
            .And.OnlyContain(group => group.GetProperty("points").GetArrayLength() == 1);
        snapshots[0].GetProperty("products").EnumerateArray().Should().OnlyContain(product =>
            product.GetProperty("completeness").GetProperty("score").ValueKind == JsonValueKind.Null
            && !product.GetProperty("completeness").GetProperty("partial").GetBoolean());
    }

    /// <summary>The status once loading has started downloading a file of the snapshot dated <paramref name="date"/>.</summary>
    private static async Task<IngestionStatusSnapshot> WaitForDownloadOfAsync(ExplorerApiFactory factory, string date, CancellationToken ct)
    {
        await foreach (var status in factory.Status.WatchAsync(ct))
        {
            if (status.CurrentFile?.Contains(date, StringComparison.Ordinal) == true)
            {
                return status;
            }
        }

        throw new InvalidOperationException("The status stream ended first.");
    }
}
