using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PriceExplorer.Api.Core.Ingestion;
using PriceExplorer.Tests.Support;

namespace PriceExplorer.Tests.Api;

public sealed class LoadingStateTests
{
    [Fact(Timeout = 60_000)]
    public async Task While_data_is_loading_searches_answer_503_with_retry_after_and_status_says_why()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = ExplorerApiFactory.With(http => http.Hold(FixtureHttpHandler.AmbetterUrl));
        using var client = factory.CreateClient();

        // Event-driven: wait until the loader is downloading the held file.
        await foreach (var status in factory.Status.WatchAsync(ct))
        {
            if (status.Phase == IngestionPhase.Downloading && status.CurrentFile is not null)
            {
                break;
            }
        }

        using var search = await client.GetAsync("/api/v1/rates?code=45378", ct);
        using var ready = await client.GetAsync("/health/ready", ct);
        using var invalid = await client.GetAsync("/api/v1/rates?code=!!!", ct);

        search.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        search.Headers.RetryAfter.Should().NotBeNull();
        var problem = await search.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("code").GetString().Should().Be("index_loading");
        ready.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest, "bad input is reported even while data loads");
    }
}
