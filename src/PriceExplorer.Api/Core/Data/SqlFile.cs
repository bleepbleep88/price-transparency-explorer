using System.Collections.Concurrent;
using System.Reflection;

namespace PriceExplorer.Api.Core.Data;

/// <summary>
/// Reads .sql files embedded next to the code that runs them (e.g. Core/Handlers/Snapshots/ListSnapshots.sql),
/// so every query is readable SQL rather than a C# string.
/// </summary>
internal static class SqlFile
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    /// <summary>Loads <paramref name="fileName"/> from the same folder (namespace) as <typeparamref name="TOwner"/>.</summary>
    public static string For<TOwner>(string fileName) =>
        Load(typeof(TOwner).Assembly, $"{typeof(TOwner).Namespace}.{fileName}");

    private static string Load(Assembly assembly, string resourceName) =>
        Cache.GetOrAdd(resourceName, name =>
        {
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded SQL file '{name}' was not found.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });
}
