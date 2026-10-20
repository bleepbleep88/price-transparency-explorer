using System.Text.Json;
using System.Text.RegularExpressions;
using PriceExplorer.Api.Core.Models.Rates;

namespace PriceExplorer.Api.Core.Reference;

/// <summary>
/// Whether a provider directory is loaded, and where it came from (reference/provider_directory.source.json, written by
/// the builder tool), so the UI can cite the registry release instead of hardcoding it. Set at startup by
/// <see cref="ReferenceDataLoader"/>.
/// </summary>
public sealed partial class ProviderDirectoryInfo
{
    private volatile DirectorySource? _source;
    private volatile IReadOnlyList<LocationOption> _locations = [];

    /// <summary>Null until a directory loads, and when the file is missing or unreadable.</summary>
    public DirectorySource? Source => _source;

    /// <summary>A directory with at least one NPI is loaded, so rows can say who is behind them.</summary>
    public bool IsLoaded => _source is { NpisLoaded: > 0 };

    /// <summary>New York counties and NYC boroughs in the directory, for the location filter (Locations.sql). Empty until it loads.</summary>
    public IReadOnlyList<LocationOption> Locations => _locations;

    internal void Set(DirectorySource? source, IReadOnlyList<LocationOption>? locations = null)
    {
        _locations = locations ?? [];
        _source = source;
    }

    /// <summary>
    /// A loaded directory of <paramref name="npisLoaded"/> NPIs, described by the source file next to it. Without a
    /// readable source file, the release and build date are unknown.
    /// </summary>
    internal static DirectorySource Describe(string sourcePath, long npisLoaded)
    {
        try
        {
            if (File.Exists(sourcePath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(sourcePath));
                var root = document.RootElement;
                var nppes = root.TryGetProperty("nppes", out var url) ? url.GetString() : null;
                var match = nppes is null ? Match.Empty : ReleaseName().Match(nppes);
                DateTimeOffset? builtAt = root.TryGetProperty("generatedAt", out var generated) && generated.TryGetDateTimeOffset(out var at) ? at : null;
                return new DirectorySource(
                    Release: match.Success ? $"{match.Groups["month"].Value} {match.Groups["year"].Value}" : null,
                    BuiltAt: builtAt,
                    NpisLoaded: npisLoaded,
                    Source: nppes);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
        {
            // A damaged source note only loses the citation; the directory itself loaded fine.
        }

        return new DirectorySource(null, null, npisLoaded, null);
    }

    // NPPES_Data_Dissemination_October_2026_V2.zip → October 2026
    [GeneratedRegex(@"Dissemination_(?<month>[A-Za-z]+)_(?<year>\d{4})", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseName();
}
