using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using PriceExplorer.Api.Core.Handlers.Status;
using PriceExplorer.Api.Core.Ingestion;
using PriceExplorer.Api.Core.Queries.Status;
using PriceExplorer.Tests.Support;

namespace PriceExplorer.Tests.Api;

/// <summary>First load, stopping mid-load, and running with loading turned off.</summary>
public sealed class LoadingLifecycleTests
{
    private static readonly Dictionary<string, string?> LoadingOff = new() { ["Ingestion:Enabled"] = "false" };

    [Fact(Timeout = 60_000)]
    public async Task On_the_first_load_the_snapshot_is_shown_as_soon_as_its_first_file_is_saved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = ExplorerApiFactory.With(http => http.Hold(FixtureHttpHandler.EssentialUrl));
        using var client = factory.CreateClient();
        await WaitForDataAsync(factory, ct);

        var snapshot = (await client.GetFromJsonAsync<JsonElement>("/api/v1/snapshots", ct)).EnumerateArray().Single();
        using var ready = await client.GetAsync("/health/ready", ct);

        snapshot.GetProperty("status").GetString().Should().Be("loading");
        snapshot.GetProperty("isLatest").GetBoolean().Should().BeTrue("readiness and the default snapshot follow the same rule");
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(Timeout = 90_000)]
    public async Task Stopping_mid_load_settles_the_snapshot_and_a_restart_with_loading_off_serves_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("pte-stop-").FullName;
        try
        {
            await using (var first = ExplorerApiFactory.With(http => http.Hold(FixtureHttpHandler.EssentialUrl), directory))
            {
                _ = first.Services;
                await WaitForDataAsync(first, ct);
            }

            await using var second = ExplorerApiFactory.With(dataDirectory: directory, settings: LoadingOff);
            using var client = second.CreateClient();
            var status = await second.WaitForIngestionAsync(ct);
            var snapshot = (await client.GetFromJsonAsync<JsonElement>("/api/v1/snapshots", ct)).EnumerateArray().Single();

            status.Phase.Should().Be(IngestionPhase.Ready);
            status.DataAvailable.Should().BeTrue();
            status.Message.Should().Be("Loading is turned off; showing saved data.");
            snapshot.GetProperty("status").GetString().Should().Be("partial", "the stop happened after one of its two files was saved");
            snapshot.GetProperty("isLatest").GetBoolean().Should().BeTrue();
            second.Http.Requests.Should().BeEmpty("loading is turned off");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task With_loading_off_and_no_saved_data_the_status_says_so_and_the_api_still_answers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = ExplorerApiFactory.With(settings: LoadingOff);
        using var client = factory.CreateClient();

        var status = await factory.WaitForIngestionAsync(ct);
        var snapshots = await client.GetFromJsonAsync<JsonElement>("/api/v1/snapshots", ct);

        status.Phase.Should().Be(IngestionPhase.Failed);
        status.Message.Should().Be("Loading is turned off and there is no saved data.");
        snapshots.GetArrayLength().Should().Be(0);
    }

    [Fact(Timeout = 10_000)]
    public async Task The_status_stream_ends_when_the_app_starts_stopping()
    {
        var lifetime = new StubLifetime();
        var handler = new WatchIngestionStatusHandler(new IngestionStatus(TimeProvider.System), lifetime);
        var received = new List<IngestionStatusSnapshot>();

        await using var stream = handler.Handle(new WatchIngestionStatusQuery(), CancellationToken.None).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        (await stream.MoveNextAsync()).Should().BeTrue("the current status is sent first");
        received.Add(stream.Current);

        lifetime.Stop();

        (await stream.MoveNextAsync()).Should().BeFalse("an open stream must not hold up shutdown");
        received.Should().ContainSingle().Which.Phase.Should().Be(IngestionPhase.Starting);
    }

    private static async Task WaitForDataAsync(ExplorerApiFactory factory, CancellationToken ct)
    {
        await foreach (var status in factory.Status.WatchAsync(ct))
        {
            if (status.DataAvailable)
            {
                return;
            }
        }
    }

    private sealed class StubLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => _stopping.Cancel();

        public void Stop() => StopApplication();
    }
}
