using System.Net;
using PriceExplorer.Tests.Support;

namespace PriceExplorer.Tests.Api;

public sealed class HealthEndpointTests(ExplorerApiFactory factory) : IClassFixture<ExplorerApiFactory>
{
    [Fact]
    public async Task Live_health_check_answers_ok_as_soon_as_the_process_is_up()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unknown_routes_return_a_problem_response()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/does-not-exist", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }
}
