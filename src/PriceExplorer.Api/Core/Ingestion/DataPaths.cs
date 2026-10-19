using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PriceExplorer.Api.Configuration;

namespace PriceExplorer.Api.Core.Ingestion;

/// <summary>Resolves where downloads and the database live on disk.</summary>
public sealed class DataPaths
{
    public DataPaths(IOptions<IngestionOptions> options, IHostEnvironment environment)
    {
        Root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.DataDirectory));
        Downloads = Path.Combine(Root, "downloads");
        Database = Path.Combine(Root, "explorer.duckdb");
        Directory.CreateDirectory(Downloads);
    }

    public string Root { get; }

    public string Downloads { get; }

    public string Database { get; }

    /// <summary>Cache file for a URL: a short hash (so equal file names from different paths never collide) plus the original name.</summary>
    public string CachePathFor(string url)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..12];
        var name = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? Path.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath)) : "file";
        var safe = string.Concat(name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
        return Path.Combine(Downloads, $"{hash}-{(safe.Length > 0 ? safe : "file")}");
    }
}
