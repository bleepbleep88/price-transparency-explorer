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
