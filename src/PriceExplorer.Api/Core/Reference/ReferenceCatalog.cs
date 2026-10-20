using System.Collections.Frozen;

namespace PriceExplorer.Api.Core.Reference;

/// <summary>
/// Plain-language labels for official code sets, loaded once from the embedded .tsv files in this folder:
/// CMS place-of-service codes (grouped into care settings), revenue-code categories, and modifier meanings.
/// Revenue-code and modifier wording is our own short description, not licensed NUBC/AMA text.
/// </summary>
public sealed class ReferenceCatalog
{
    private readonly FrozenDictionary<string, SettingInfo> _settings;
    private readonly FrozenDictionary<string, PlaceInfo> _places;
    private readonly FrozenDictionary<string, string> _revenueCodes;
    private readonly FrozenDictionary<string, ModifierInfo> _modifiers;

    public ReferenceCatalog()
    {
        _settings = ReadTsv("settings.tsv")
            .ToFrozenDictionary(
                row => row[0],
                row => new SettingInfo(row[0], row[1], NullIfEmpty(row[2]), int.Parse(row[3], System.Globalization.CultureInfo.InvariantCulture)),
                StringComparer.Ordinal);
        _places = ReadTsv("place_of_service.tsv")
            .ToFrozenDictionary(row => row[0], row => new PlaceInfo(row[0], row[1], row[2]), StringComparer.OrdinalIgnoreCase);
        _revenueCodes = ReadTsv("revenue_codes.tsv")
            .ToFrozenDictionary(row => row[0], row => row[1], StringComparer.Ordinal);
        _modifiers = ReadTsv("modifiers.tsv")
            .ToFrozenDictionary(row => row[0], row => new ModifierInfo(row[0], row[1], NullIfEmpty(row[2])), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Care setting for a place-of-service code ('' = not stated in the file).</summary>
    public SettingInfo SettingForPlace(string pos)
    {
        if (pos.Length == 0)
        {
            return _settings["unspecified"];
        }

        return _places.TryGetValue(pos, out var place) ? _settings[place.SettingKey] : _settings["other"];
    }

    public SettingInfo? Setting(string key) => _settings.GetValueOrDefault(key);

    public IEnumerable<SettingInfo> Settings => _settings.Values.OrderBy(s => s.SortOrder);

    public string PlaceName(string pos) =>
        pos.Length == 0 ? "Not stated" : _places.TryGetValue(pos, out var place) ? place.Name : $"Place of service {pos} (unrecognized)";

    /// <summary>Label for a 4-digit revenue code: exact code first, then its 3-digit category.</summary>
    public string RevenueCodeLabel(string code)
    {
        if (_revenueCodes.TryGetValue(code, out var exact))
        {
            return exact;
        }

        return code.Length >= 3 && _revenueCodes.TryGetValue(code[..3], out var category) ? category : "Facility service";
    }

    public ModifierInfo Modifier(string code) =>
        _modifiers.TryGetValue(code, out var modifier) ? modifier : new ModifierInfo(code, $"Modifier {code}", null);

    private static List<string[]> ReadTsv(string fileName)
    {
        var type = typeof(ReferenceCatalog);
        using var stream = type.Assembly.GetManifestResourceStream($"{type.Namespace}.{fileName}")
            ?? throw new InvalidOperationException($"Embedded reference file '{fileName}' was not found.");
        using var reader = new StreamReader(stream);

        var rows = new List<string[]>();
        var header = reader.ReadLine()?.Split('\t').Length ?? 0;
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var cells = line.Split('\t');
            if (cells.Length < header)
            {
                Array.Resize(ref cells, header);
                for (var i = 0; i < cells.Length; i++)
                {
                    cells[i] ??= "";
                }
            }

            rows.Add(cells);
        }

        return rows;
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}

public sealed record SettingInfo(string Key, string Label, string? Note, int SortOrder);

public sealed record PlaceInfo(string Pos, string Name, string SettingKey);

/// <param name="Component">"professional" (26) or "technical" (TC): the rate covers only part of the service.</param>
public sealed record ModifierInfo(string Code, string Label, string? Component);
