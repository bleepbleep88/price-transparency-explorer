using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PriceExplorer.Tests.Support;

namespace PriceExplorer.Tests.Insights;

/// <summary>What the insights say before their statistics exist, and what the API document promises about them.</summary>
public sealed class InsightsLifecycleTests
{
    [Fact(Timeout = 60_000)]
    public async Task Before_the_statistics_are_built_insights_say_they_are_not_current()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = ExplorerApiFactory.With(http => http.Hold(FixtureHttpHandler.EssentialUrl));
        using var client = factory.CreateClient();

        // The Ambetter file is saved, so data is available; statistics wait for the held Essential Plan file.
        await foreach (var status in factory.Status.WatchAsync(ct))
        {
            if (status.DataAvailable)
            {
                break;
            }
        }

        var gaps = await client.GetFromJsonAsync<JsonElement>("/api/v1/insights/gaps", ct);
        var scorecard = await client.GetFromJsonAsync<JsonElement>("/api/v1/insights/providers/46-0857425", ct);

        foreach (var result in new[] { gaps, scorecard })
        {
            result.GetProperty("statistics").GetProperty("isCurrent").GetBoolean().Should().BeFalse();
            result.GetProperty("statistics").GetProperty("builtAt").ValueKind.Should().Be(JsonValueKind.Null);
            result.GetProperty("statistics").GetProperty("snapshot").GetProperty("indexDate").GetString().Should().Be("2026-08-31");
        }

        gaps.GetProperty("rows").GetProperty("total").GetInt32().Should().Be(0);
        scorecard.GetProperty("provider").GetProperty("name").GetString().Should().NotBeNullOrEmpty();
        scorecard.GetProperty("groups").GetArrayLength().Should().Be(0);

        using var compare = await client.GetAsync("/api/v1/insights/compare", ct);
        compare.StatusCode.Should().Be(HttpStatusCode.BadRequest, "one product is saved so far");
        (await compare.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be("pick_products");
    }

    [Fact(Timeout = 60_000)]
    public async Task While_nothing_is_saved_insights_answer_503_like_search()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = ExplorerApiFactory.With(http => http.Hold(FixtureHttpHandler.AmbetterUrl));
        using var client = factory.CreateClient();

        foreach (var url in new[] { "/api/v1/insights/gaps", "/api/v1/insights/providers/46-0857425", "/api/v1/insights/compare" })
        {
            using var response = await client.GetAsync(url, ct);
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, url);
            response.Headers.RetryAfter.Should().NotBeNull();
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task The_api_document_lists_the_insights_with_camel_case_parameters_and_problem_responses()
    {
        await using var factory = new ExplorerApiFactory();
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", TestContext.Current.CancellationToken);
        var paths = document.GetProperty("paths");

        Parameters(paths, "/api/v1/insights/gaps").Should().Contain(["product", "category", "setting", "feeType", "code", "sort", "page", "pageSize"]);
        Parameters(paths, "/api/v1/insights/compare").Should().Contain(["product", "versus", "code", "category", "setting", "sort"]);
        Parameters(paths, "/api/v1/insights/providers/{provider}").Should().Contain(["provider", "snapshot"]);
        foreach (var path in new[] { "/api/v1/insights/gaps", "/api/v1/insights/compare", "/api/v1/insights/providers/{provider}" })
        {
            Parameters(paths, path).Should().NotContain(name => char.IsUpper(name[0]), path);
            paths.GetProperty(path).GetProperty("get").GetProperty("responses").GetProperty("400").GetProperty("content")
                .EnumerateObject().Select(content => content.Name).Should().Equal("application/problem+json");
        }
    }

    private static List<string> Parameters(JsonElement paths, string path) =>
        paths.GetProperty(path).GetProperty("get").GetProperty("parameters").EnumerateArray()
            .Select(parameter => parameter.GetProperty("name").GetString()!).ToList();
}
