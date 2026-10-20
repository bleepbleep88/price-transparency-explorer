using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace PriceExplorer.Tests.Support;

/// <summary>Stands in for the payer's web server: serves fixture files with ETags (and 304s), or failures on demand.</summary>
public sealed class FixtureHttpHandler : HttpMessageHandler
{
    public const string BaseUrl = "https://fixtures.test/";
    public const string IndexUrl = BaseUrl + "2026-08-31_fidelis_index.json";
    public const string AmbetterUrl = BaseUrl + "2026-08-31_fidelis-ex_in-network.json";
    public const string EssentialUrl = BaseUrl + "2026-08-31_fidelis-es_in-network.json";
    public const string AmbetterAllowedUrl = BaseUrl + "2026-08-31_fidelis-ex_allowed-amounts.json";
    public const string EssentialAllowedUrl = BaseUrl + "2026-08-31_fidelis-es_allowed-amounts.json";

    private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new(StringComparer.OrdinalIgnoreCase);

    public FixtureHttpHandler()
    {
        ServeFixture(IndexUrl, "2026-08-31_fidelis_index.json");
        ServeFixture(AmbetterUrl, "2026-08-31_fidelis-ex_in-network.json");
        ServeFixture(EssentialUrl, "2026-08-31_fidelis-es_in-network.json");
        ServeFixture(AmbetterAllowedUrl, "2026-08-31_fidelis-ex_allowed-amounts.json");
        ServeFixture(EssentialAllowedUrl, "2026-08-31_fidelis-es_allowed-amounts.json");
    }

    /// <summary>Every request seen: URL and whether it was a conditional (If-None-Match) re-check.</summary>
    public ConcurrentQueue<(string Url, bool Conditional)> Requests { get; } = new();

    public void ServeFixture(string url, string fixtureName) => ServeBytes(url, File.ReadAllBytes(TestFiles.Fixture(fixtureName)));

    /// <summary>Serves JSON written by the test, for index shapes the real fixtures don't have.</summary>
    public void ServeJson(string url, string json) => ServeBytes(url, Encoding.UTF8.GetBytes(json));

    private void ServeBytes(string url, byte[] bytes)
    {
        var etag = $"\"{Convert.ToHexString(SHA256.HashData(bytes))[..16]}\"";
        _routes[url] = request =>
        {
            if (request.Headers.IfNoneMatch.Any(tag => tag.Tag == etag))
            {
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            response.Content.Headers.LastModified = new DateTimeOffset(2026, 9, 4, 18, 23, 42, TimeSpan.Zero);
            response.Headers.ETag = new EntityTagHeaderValue(etag);
            return response;
        };
    }

    /// <summary>Answers like Centene does for a missing file: an error status with an HTML page.</summary>
    public void Fail(string url, HttpStatusCode status) =>
        _routes[url] = _ => new HttpResponseMessage(status)
        {
            Content = new StringContent("<html><body>Page not found</body></html>", Encoding.UTF8, "text/html"),
        };

    /// <summary>Requests to this URL hang until the test ends, so the app stays in its "still loading" state.</summary>
    public void Hold(string url) => _held[url] = true;

    /// <summary>Completes when the app first requests this URL, so a test can act at that point without polling.</summary>
    public Task WhenRequested(string url) => Requested(url).Task;

    private readonly ConcurrentDictionary<string, bool> _held = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _requested = new(StringComparer.OrdinalIgnoreCase);

    private TaskCompletionSource Requested(string url) =>
        _requested.GetOrAdd(url, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        Requests.Enqueue((url, request.Headers.IfNoneMatch.Count > 0));
        Requested(url).TrySetResult();

        if (_held.ContainsKey(url))
        {
            // Completes only when the app shuts down and cancels the request.
            var never = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using (cancellationToken.Register(() => never.TrySetCanceled(cancellationToken)))
            {
                return await never.Task;
            }
        }

        var response = _routes.TryGetValue(url, out var route)
            ? route(request)
            : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("<html>404</html>", Encoding.UTF8, "text/html") };
        response.RequestMessage = request;
        return response;
    }
}
