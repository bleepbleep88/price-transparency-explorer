using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PriceExplorer.Api.Core.Ingestion;
using PriceExplorer.Tests.Support;

namespace PriceExplorer.Tests.Api;

public sealed class IngestionApiTests(ExplorerApiFactory factory) : IClassFixture<ExplorerApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact(Timeout = 60_000)]
    public async Task Every_rate_file_the_index_lists_is_loaded_with_its_source_details()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);

        var snapshots = await client.GetFromJsonAsync<JsonElement>("/api/v1/snapshots", ct);

        var snapshot = snapshots.EnumerateArray().Should().ContainSingle().Subject;
        snapshot.GetProperty("indexDate").GetString().Should().Be("2026-08-31");
        snapshot.GetProperty("status").GetString().Should().Be("ready");
        snapshot.GetProperty("isLatest").GetBoolean().Should().BeTrue();

        var products = snapshot.GetProperty("products").EnumerateArray().ToList();
        products.Select(p => p.GetProperty("productLabel").GetString()).Should().Equal("Ambetter from Fidelis Care", "Essential Plan");

        var ambetter = products[0];
        ambetter.GetProperty("status").GetString().Should().Be("loaded");
        ambetter.GetProperty("url").GetString().Should().Be(FixtureHttpHandler.AmbetterUrl);
        ambetter.GetProperty("fileLastUpdatedOn").GetString().Should().Be("2026-09-02");
        ambetter.GetProperty("pricesPublished").GetInt64().Should().Be(27);
        ambetter.GetProperty("uniqueRates").GetInt32().Should().Be(23);
        ambetter.GetProperty("duplicatesRemoved").GetInt64().Should().Be(4);
        ambetter.GetProperty("sha256").GetString().Should().HaveLength(64);

        products[1].GetProperty("uniqueRates").GetInt32().Should().Be(8);
    }

    [Fact(Timeout = 60_000)]
    public async Task Status_reports_ready_and_the_readiness_check_passes()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);

        var status = await client.GetFromJsonAsync<IngestionStatusSnapshot>("/api/v1/status", Json, ct);
        using var ready = await client.GetAsync("/health/ready", ct);

        status!.DataAvailable.Should().BeTrue();
        status.FilesDone.Should().Be(2);
        status.FilesTotal.Should().Be(2);
        status.Issues.Should().BeEmpty();
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(Timeout = 60_000)]
    public async Task The_status_stream_sends_the_current_status_as_a_server_sent_event()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);

        using var response = await client.GetAsync("/api/v1/status/stream", HttpCompletionOption.ResponseHeadersRead, ct);
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(body);

        string? eventName = null;
        string? data = null;
        while (data is null && await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.StartsWith("event:", StringComparison.Ordinal)) eventName = line["event:".Length..].Trim();
            if (line.StartsWith("data:", StringComparison.Ordinal)) data = line["data:".Length..].Trim();
        }

        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");
        eventName.Should().Be("status");
        using var json = JsonDocument.Parse(data!);
        json.RootElement.GetProperty("phase").GetString().Should().Be("ready");
        json.RootElement.GetProperty("dataAvailable").GetBoolean().Should().BeTrue();
    }

    [Fact(Timeout = 60_000)]
    public async Task Plans_are_grouped_by_product_with_every_published_name()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);

        var plans = await client.GetFromJsonAsync<JsonElement>("/api/v1/snapshots/1/plans", ct);

        var products = plans.GetProperty("products").EnumerateArray().ToList();
        products.Should().HaveCount(2);
        products[0].GetProperty("planEntries").GetInt32().Should().Be(4);
        products[1].GetProperty("plans").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("planId").GetString().Should().Be("25303EP000");
    }
}
