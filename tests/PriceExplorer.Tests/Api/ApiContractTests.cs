using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PriceExplorer.Tests.Support;

namespace PriceExplorer.Tests.Api;

/// <summary>Input rules, the error shape whatever the client accepts, and what the OpenAPI document promises.</summary>
public sealed class ApiContractTests(ExplorerApiFactory factory) : IClassFixture<ExplorerApiFactory>
{
    [Theory(Timeout = 60_000)]
    [InlineData("product=essential-plan%00", "product")]
    [InlineData("product=Essential%20Plan", "product")]
    [InlineData("group=a%7Cb", "group")]
    [InlineData("group=essential-plan%7CCPT%7C45378%7Cprofessional%7Casc%7C33%7Cnegotiated%00", "group")]
    public async Task Product_and_group_must_be_keys_from_the_snapshot(string filter, string field)
    {
        using var response = await GetAsync($"/api/v1/rates?code=45378&{filter}", TestContext.Current.CancellationToken);

        (await ErrorsAsync(response)).Should().ContainKey(field);
    }

    [Fact(Timeout = 60_000)]
    public async Task Any_provider_key_from_results_filters_a_search()
    {
        using var response = await GetAsync("/api/v1/rates?code=45378&ein=npi:1234567890", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a tax ID that isn't an EIN is keyed type:value, and results can carry that key");
    }

    [Fact(Timeout = 60_000)]
    public async Task Control_characters_in_typeahead_text_get_their_own_message()
    {
        var ct = TestContext.Current.CancellationToken;

        using var providers = await GetAsync("/api/v1/providers?q=%00%00", ct);
        using var codes = await GetAsync("/api/v1/codes?q=%00", ct);

        (await ErrorsAsync(providers))["q"].Should().Equal("Search text can't contain control characters.");
        (await ErrorsAsync(codes))["q"].Should().Equal("Search text can't contain control characters.");
    }

    [Fact(Timeout = 60_000)]
    public async Task Too_long_names_get_a_too_long_message()
    {
        var ct = TestContext.Current.CancellationToken;
        var name = new string('x', 101);

        using var search = await GetAsync($"/api/v1/rates?code=45378&provider={name}", ct);
        using var typeahead = await GetAsync($"/api/v1/providers?q={name}", ct);

        (await ErrorsAsync(search))["provider"].Should().Equal("A provider name is at most 100 characters.");
        (await ErrorsAsync(typeahead))["q"].Should().Equal("Search text is at most 100 characters.");
    }

    [Theory(Timeout = 60_000)]
    [InlineData("text/plain")]
    [InlineData("text/html")]
    [InlineData("application/xml")]
    public async Task Errors_are_problem_json_whatever_the_client_accepts(string accept)
    {
        var ct = TestContext.Current.CancellationToken;
        using var invalid = await GetAsync("/api/v1/rates?code=", ct, accept);
        using var missingRate = await GetAsync("/api/v1/rates/1/source", ct, accept);
        using var badRoute = await GetAsync("/api/v1/rates/abc/source", ct, accept);

        (await ErrorsAsync(invalid)).Should().ContainKey("code");
        using var badBinding = await GetAsync("/api/v1/rates?code=45378&page=abc", ct, accept);
        (await ProblemAsync(badBinding, HttpStatusCode.BadRequest)).GetProperty("type").GetString()
            .Should().Be("https://tools.ietf.org/html/rfc9110#section-15.5.1", "binding errors have the same shape as validator errors");
        var notFound = await ProblemAsync(missingRate, HttpStatusCode.NotFound);
        notFound.GetProperty("code").GetString().Should().Be("rate_not_found");
        notFound.GetProperty("type").GetString().Should().Be("https://tools.ietf.org/html/rfc9110#section-15.5.5");
        (await ProblemAsync(badRoute, HttpStatusCode.NotFound)).GetProperty("code").GetString().Should().Be("not_found");
    }

    [Fact(Timeout = 60_000)]
    public async Task The_openapi_document_names_parameters_the_way_hint_links_do()
    {
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", TestContext.Current.CancellationToken);

        var search = document.GetProperty("paths").GetProperty("/api/v1/rates").GetProperty("get");
        search.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString())
            .Should().Contain(["code", "codeType", "npi", "ein", "product", "setting", "feeType", "provider", "group", "page", "pageSize"])
            .And.NotContain(name => char.IsUpper(name![0]));
        search.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "code")
            .GetProperty("required").GetBoolean().Should().BeTrue();
        search.GetProperty("responses").GetProperty("400").GetProperty("content").EnumerateObject().Select(c => c.Name)
            .Should().Equal("application/problem+json");
        search.GetProperty("responses").GetProperty("200").GetProperty("content").EnumerateObject().Select(c => c.Name)
            .Should().Equal("application/json");
        foreach (var typeahead in new[] { "/api/v1/codes", "/api/v1/providers" })
        {
            document.GetProperty("paths").GetProperty(typeahead).GetProperty("get").GetProperty("parameters").EnumerateArray()
                .Single(p => p.GetProperty("name").GetString() == "q").GetProperty("required").GetBoolean().Should().BeTrue(typeahead);
        }

        var linkValues = document.GetProperty("components").GetProperty("schemas").GetProperty("HintLink")
            .GetProperty("properties").GetProperty("query").GetProperty("additionalProperties").GetProperty("type");
        linkValues.EnumerateArray().Select(t => t.GetString()).Should().Contain("null", "a null value removes that filter");

        document.GetProperty("paths").GetProperty("/api/v1/snapshots/{snapshotId}/plans").GetProperty("get")
            .GetProperty("responses").TryGetProperty("400", out _).Should().BeTrue();
    }

    private async Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct, string accept = "application/json")
    {
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        return await client.SendAsync(request, ct);
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private static async Task<Dictionary<string, string[]>> ErrorsAsync(HttpResponseMessage response)
    {
        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest);
        problem.GetProperty("code").GetString().Should().Be("validation_failed");
        return problem.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
    }
}
