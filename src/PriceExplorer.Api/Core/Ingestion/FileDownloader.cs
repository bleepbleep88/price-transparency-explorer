using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PriceExplorer.Api.Configuration;

namespace PriceExplorer.Api.Core.Ingestion;

/// <summary>
/// Downloads TiC files into a local cache.
/// <list type="bullet">
/// <item>Re-checks a cached file with a conditional GET (ETag / Last-Modified); a 304 reuses the cache.</item>
/// <item>Interrupted downloads resume from the bytes on disk (Range + If-Range), so a retry never starts over.</item>
/// <item>A stall watchdog cancels an attempt when no bytes arrive for the configured time.</item>
/// <item>If every attempt fails and a cached copy exists, the cached copy is used and the caller is warned.</item>
/// </list>
/// </summary>
internal sealed class FileDownloader(
    IHttpClientFactory httpClientFactory,
    IOptions<IngestionOptions> options,
    DataPaths paths,
    ILogger<FileDownloader> logger)
{
    public const string HttpClientName = "tic-files";

    private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<DownloadedFile> FetchAsync(string url, Action<long, long?>? onProgress, CancellationToken cancellationToken)
    {
        var target = paths.CachePathFor(url);
        var manifestPath = target + ".manifest.json";
        var cached = await ReadManifestAsync(manifestPath, target, cancellationToken);
        var client = httpClientFactory.CreateClient(HttpClientName);
        var settings = options.Value;
        var started = DateTimeOffset.UtcNow;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= settings.MaxDownloadAttempts; attempt++)
        {
            try
            {
                var result = await FetchOnceAsync(client, url, target, cached, onProgress, cancellationToken);
                if (!result.FromCache)
                {
                    await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(result.ToManifest(), ManifestJson), cancellationToken);
                }

                return result with { Elapsed = DateTimeOffset.UtcNow - started };
            }
            catch (PermanentDownloadException ex)
            {
                lastError = ex;
                break;
            }
            catch (Exception ex) when (IsTransient(ex, cancellationToken))
            {
                lastError = ex;
                logger.LogWarning("Download attempt {Attempt}/{Max} for {Url} failed: {Error}. Resuming.",
                    attempt, settings.MaxDownloadAttempts, url, ex.Message);
            }
        }

        if (cached is not null)
        {
            logger.LogWarning("Using cached copy of {Url}: {Error}", url, lastError?.Message);
            return cached with
            {
                Elapsed = DateTimeOffset.UtcNow - started,
                Warning = $"Couldn't re-check the file with the payer ({lastError?.Message}); using the copy downloaded {cached.FetchedAt:yyyy-MM-dd HH:mm} UTC.",
            };
        }

        throw new DownloadFailedException(url, lastError);
    }

    private async Task<DownloadedFile> FetchOnceAsync(
        HttpClient client, string url, string target, DownloadedFile? cached, Action<long, long?>? onProgress, CancellationToken cancellationToken)
    {
        var partial = target + ".partial";
        var partialEtagPath = partial + ".etag";
        var resumeFrom = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        var partialEtag = resumeFrom > 0 && File.Exists(partialEtagPath) ? await File.ReadAllTextAsync(partialEtagPath, cancellationToken) : null;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (resumeFrom > 0 && !string.IsNullOrEmpty(partialEtag))
        {
            request.Headers.Range = new RangeHeaderValue(resumeFrom, null);
            request.Headers.IfRange = new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(partialEtag));
        }
        else if (cached is not null)
        {
            if (!string.IsNullOrEmpty(cached.ETag))
            {
                request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(cached.ETag));
            }

            if (cached.LastModified is { } lastModified)
            {
                request.Headers.IfModifiedSince = lastModified;
            }
        }

        using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stall = TimeSpan.FromSeconds(options.Value.StallTimeoutSeconds);
        watchdog.CancelAfter(stall);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, watchdog.Token);

        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
        {
            return cached with { FromCache = true, Revalidated = true };
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var message = $"{url} answered HTTP {status} {response.ReasonPhrase}";
            throw status is 408 or 429 or >= 500 ? new HttpRequestException(message) : new PermanentDownloadException(message);
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is "text/html")
        {
            throw new PermanentDownloadException($"{url} returned a web page instead of a data file");
        }

        var appending = response.StatusCode == HttpStatusCode.PartialContent;
        if (!appending)
        {
            resumeFrom = 0;
            var etag = response.Headers.ETag?.ToString();
            if (etag is not null)
            {
                await File.WriteAllTextAsync(partialEtagPath, etag, cancellationToken);
            }
        }

        long? total = response.Content.Headers.ContentLength is { } length ? length + resumeFrom : null;

        await using (var body = await response.Content.ReadAsStreamAsync(watchdog.Token))
        await using (var output = new FileStream(partial, appending ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
        {
            var buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
            try
            {
                var written = resumeFrom;
                while (true)
                {
                    watchdog.CancelAfter(stall);
                    var read = await body.ReadAsync(buffer, watchdog.Token);
                    if (read == 0)
                    {
                        break;
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    written += read;
                    onProgress?.Invoke(written, total);
                }

                if (total is { } expected && written != expected)
                {
                    throw new IOException($"Download ended early: got {written:N0} of {expected:N0} bytes");
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        var sha256 = await ComputeSha256Async(partial, cancellationToken);
        File.Move(partial, target, overwrite: true);
        File.Delete(partialEtagPath);

        return new DownloadedFile(
            Url: url,
            LocalPath: target,
            Length: new FileInfo(target).Length,
            Sha256: sha256,
            ETag: response.Headers.ETag?.ToString(),
            LastModified: response.Content.Headers.LastModified,
            FetchedAt: DateTimeOffset.UtcNow,
            FromCache: false,
            Revalidated: false,
            Elapsed: TimeSpan.Zero,
            Warning: null);
    }

    private static async Task<DownloadedFile?> ReadManifestAsync(string manifestPath, string target, CancellationToken cancellationToken)
    {
        if (!File.Exists(manifestPath) || !File.Exists(target))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(manifestPath);
            var manifest = await JsonSerializer.DeserializeAsync<DownloadManifest>(stream, ManifestJson, cancellationToken);
            if (manifest is null || new FileInfo(target).Length != manifest.Length)
            {
                return null;
            }

            return new DownloadedFile(manifest.Url, target, manifest.Length, manifest.Sha256, manifest.ETag, manifest.LastModified,
                manifest.FetchedAt, FromCache: true, Revalidated: false, Elapsed: TimeSpan.Zero, Warning: null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException => !cancellationToken.IsCancellationRequested, // the stall watchdog fired
        HttpRequestException or IOException => true,
        _ => false,
    };
}

internal sealed record DownloadedFile(
    string Url,
    string LocalPath,
    long Length,
    string Sha256,
    string? ETag,
    DateTimeOffset? LastModified,
    DateTimeOffset FetchedAt,
    bool FromCache,
    bool Revalidated,
    TimeSpan Elapsed,
    string? Warning)
{
    public DownloadManifest ToManifest() => new(Url, Length, Sha256, ETag, LastModified, FetchedAt);
}

internal sealed record DownloadManifest(string Url, long Length, string Sha256, string? ETag, DateTimeOffset? LastModified, DateTimeOffset FetchedAt);

internal sealed class PermanentDownloadException(string message) : Exception(message);

internal sealed class DownloadFailedException(string url, Exception? inner)
    : Exception($"Couldn't download {url}: {inner?.Message ?? "unknown error"}", inner);
