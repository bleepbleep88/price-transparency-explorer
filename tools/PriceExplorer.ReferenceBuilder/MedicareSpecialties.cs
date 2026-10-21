using System.Text.Json;
using System.Text.RegularExpressions;

namespace PriceExplorer.ReferenceBuilder;

/// <summary>
/// Specialty names from CMS's Medicare Provider and Supplier Taxonomy Crosswalk (public domain): each taxonomy code's
/// Medicare specialty, in CMS's words. Only CMS's own columns are read, the Medicare specialty code and its
/// provider/supplier type description. The crosswalk's taxonomy description column is the NUCC's text and is never read.
/// </summary>
internal static partial class MedicareSpecialties
{
    private const string CmsCatalog = "https://data.cms.gov/data.json";
    private const string Title = "Medicare Provider and Supplier Taxonomy Crosswalk";

    /// <summary>
    /// Codes CMS lists under more than one specialty where the most specific one reads wrong. Each pick is one of CMS's
    /// own specialties for that code.
    /// </summary>
    private static readonly Dictionary<string, string> Picks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["207L00000X"] = "05", // Anesthesiology (CMS also lists the code under Addiction Medicine)
        ["2086S0129X"] = "77", // Vascular Surgery, not Peripheral Vascular Disease
        ["207QG0300X"] = "38", // Geriatric Medicine, not Geriatric Psychiatry
        ["207QS0010X"] = "23", // Sports Medicine, not Family Practice
        ["335E00000X"] = "53", // Medical Supply Company with Orthotist-Prosthetist
        ["333600000X"] = "A5", // Pharmacy, not Medical Supply Company with Pharmacist; also the four codes below
        ["3336C0003X"] = "A5",
        ["3336L0003X"] = "A5",
        ["3336M0002X"] = "A5",
        ["3336S0011X"] = "A5",
    };

    /// <summary>The newest crosswalk's CSV download in CMS's data catalog (one dataset per release), and its date.</summary>
    public static async Task<(string Url, string Release)> FindCsvAsync(HttpClient http, CancellationToken cancellationToken)
    {
        await using var catalog = await http.GetStreamAsync(CmsCatalog, cancellationToken);
        using var document = await JsonDocument.ParseAsync(catalog, cancellationToken: cancellationToken);
        (string Url, string Release)? newest = null;
        foreach (var dataset in document.RootElement.GetProperty("dataset").EnumerateArray())
        {
            // "Medicare Provider and Supplier Taxonomy Crosswalk : 2026-08-01"
            if (dataset.GetProperty("title").GetString() is not { } title || !title.StartsWith(Title + " : ", StringComparison.Ordinal))
            {
                continue;
            }

            var release = title[(Title.Length + 3)..].Trim();
            var csv = dataset.GetProperty("distribution").EnumerateArray()
                .Where(d => d.TryGetProperty("format", out var format) && format.GetString() == "CSV" && d.TryGetProperty("downloadURL", out _))
                .Select(d => d.GetProperty("downloadURL").GetString())
                .FirstOrDefault();
            if (csv is not null && (newest is null || string.CompareOrdinal(release, newest.Value.Release) > 0))
            {
                newest = (csv, release);
            }
        }

        return newest ?? throw new InvalidDataException($"No CSV download for \"{Title}\" in {CmsCatalog}.");
    }

    /// <summary>Taxonomy code → (specialty, group) from the crosswalk CSV.</summary>
    public static Dictionary<string, (string Name, string Group)> Read(string path)
    {
        using var reader = new StreamReader(path);
        var header = CsvLine.Split(reader.ReadLine() ?? "");
        int Column(string name) => header.FindIndex(h => h.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
        var (specialtyColumn, descriptionColumn, codeColumn) =
            (Column("MEDICARE SPECIALTY CODE"), Column("MEDICARE PROVIDER/SUPPLIER TYPE DESCRIPTION"), Column("PROVIDER TAXONOMY CODE"));
        if (specialtyColumn < 0 || descriptionColumn < 0 || codeColumn < 0)
        {
            throw new InvalidDataException($"The crosswalk {Path.GetFileName(path)} is missing an expected column.");
        }

        var entries = new List<(string Code, string Specialty, string Description)>();
        while (reader.ReadLine() is { } line)
        {
            var fields = CsvLine.Split(line);
            string Field(int index) => index < fields.Count ? Footnote().Replace(fields[index], "").Trim() : "";
            if (Field(codeColumn) is { Length: > 0 } code && Field(descriptionColumn) is { Length: > 0 } description)
            {
                entries.Add((code.ToUpperInvariant(), Field(specialtyColumn), description));
            }
        }

        // The most specific reading: the specialty that covers the fewest taxonomy codes, then fixed tie-breaks.
        var breadth = entries.GroupBy(e => e.Specialty).ToDictionary(g => g.Key, g => g.Select(e => e.Code).Distinct().Count());
        return entries
            .GroupBy(e => e.Code)
            .ToDictionary(
                code => code.Key,
                code =>
                {
                    var choices = Picks.TryGetValue(code.Key, out var pick) && code.Any(e => e.Specialty == pick)
                        ? code.Where(e => e.Specialty == pick)
                        : code;
                    var chosen = choices
                        .OrderBy(e => breadth[e.Specialty])
                        .ThenBy(e => e.Specialty, StringComparer.Ordinal)
                        .ThenBy(e => e.Description, StringComparer.Ordinal)
                        .First();
                    return Display(chosen.Description);
                },
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A provider's specialty from its primary taxonomy code. A code the crosswalk doesn't list (Medicare doesn't enroll
    /// every provider type) reads "Other physician" (physician codes start with 20) or "Other provider type".
    /// </summary>
    public static (string Name, string Group) For(Dictionary<string, (string Name, string Group)> specialties, string taxonomyCode) =>
        taxonomyCode.Length == 0 ? ("", "")
        : specialties.TryGetValue(taxonomyCode, out var found) ? found
        : taxonomyCode.StartsWith("20", StringComparison.Ordinal) ? ("Other physician", Physician)
        : ("Other provider type", "");

    private const string Physician = "Physician";

    /// <summary>CMS's "Physician/Gastroenterology" → ("Gastroenterology", "Physician"); other types keep CMS's words, no group.</summary>
    private static (string Name, string Group) Display(string description)
    {
        var name = SlashSpace().Replace(description, "/");
        return name.StartsWith(Physician + "/", StringComparison.OrdinalIgnoreCase)
            ? (name[(Physician.Length + 1)..].Trim(), Physician)
            : (name, "");
    }

    /// <summary>The crosswalk marks some entries with a footnote number: "87[3]", "Physician/Undefined Physician type[6]".</summary>
    [GeneratedRegex(@"\s*\[\d+\]")]
    private static partial Regex Footnote();

    [GeneratedRegex(@"/\s+")]
    private static partial Regex SlashSpace();
}
