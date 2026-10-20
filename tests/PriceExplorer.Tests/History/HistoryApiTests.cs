using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PriceExplorer.Tests.Support;
using static PriceExplorer.Tests.Support.HistoryFiles;

namespace PriceExplorer.Tests.History;

/// <summary>Snapshots, changes and trends over three synthetic monthly snapshots (see <see cref="HistoryFiles"/>).</summary>
public sealed class HistoryApiTests(HistoryApiTests.Factory factory) : IClassFixture<HistoryApiTests.Factory>
{
    public sealed class Factory() : ExplorerApiFactory(Serve, null, null, Indexes);

    [Fact(Timeout = 60_000)]
    public async Task The_snapshot_list_flags_a_partial_upload_against_its_neighbours()
    {
        var ct = TestContext.Current.CancellationToken;
        var snapshots = await GetAsync("/api/v1/snapshots", ct);
        var ids = await IdsAsync(ct);

        var essentialJuly = Completeness(snapshots, July, Essential);
        essentialJuly.GetProperty("partial").GetBoolean().Should().BeTrue();
        essentialJuly.GetProperty("score").GetDecimal().Should().Be(0.14m, "1 unique rate against a median of 7 nearby");
        essentialJuly.GetProperty("rates").GetDecimal().Should().Be(0.14m);
        essentialJuly.GetProperty("eins").GetDecimal().Should().Be(0.2m);
        essentialJuly.GetProperty("codes").GetDecimal().Should().Be(0.5m);
        essentialJuly.GetProperty("comparedWith").EnumerateArray().Select(id => id.GetInt32()).Should().Equal(ids[August], ids[September]);

        // August's neighbours include the partial July file, which only lowers their median.
        Completeness(snapshots, August, Essential).GetProperty("score").GetDecimal().Should().Be(1.33m);
        Completeness(snapshots, August, Essential).GetProperty("partial").GetBoolean().Should().BeFalse();
        foreach (var date in new[] { July, August, September })
        {
            Completeness(snapshots, date, Ambetter).GetProperty("score").GetDecimal().Should().Be(1m);
            Completeness(snapshots, date, Ambetter).GetProperty("partial").GetBoolean().Should().BeFalse();
        }

        Snapshot(snapshots, September).GetProperty("isLatest").GetBoolean().Should().BeTrue();
    }

    private async Task<JsonElement> GetAsync(string url, CancellationToken ct)
    {
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);
        using var response = await client.GetAsync(url, ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    /// <summary>Snapshot ids by index date: they're given out in load order (newest first), so tests look them up.</summary>
    private async Task<Dictionary<string, int>> IdsAsync(CancellationToken ct) =>
        (await GetAsync("/api/v1/snapshots", ct)).EnumerateArray().ToDictionary(
            snapshot => snapshot.GetProperty("indexDate").GetString()!, snapshot => snapshot.GetProperty("snapshotId").GetInt32());

    private static JsonElement Snapshot(JsonElement snapshots, string date) =>
        snapshots.EnumerateArray().Single(snapshot => snapshot.GetProperty("indexDate").GetString() == date);

    private static JsonElement Completeness(JsonElement snapshots, string date, string product) =>
        Snapshot(snapshots, date).GetProperty("products").EnumerateArray()
            .Single(file => file.GetProperty("productKey").GetString() == product).GetProperty("completeness");
}
