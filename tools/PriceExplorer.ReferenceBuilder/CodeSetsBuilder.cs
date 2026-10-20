using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PriceExplorer.ReferenceBuilder;

/// <summary>
/// Builds the two CMS code-set files the API ships with:
/// <list type="bullet">
/// <item>reference/hcpcs_names.csv.gz: the long name of every HCPCS Level II code in CMS's newest quarterly file. Dental
/// D codes are left out (their descriptors are the American Dental Association's), and the file has no CPT codes (those
/// descriptors are the AMA's).</item>
/// <item>reference/rbcs.csv.gz: CMS's Restructured BETOS (RBCS) category, subcategory and family for every CPT and
/// HCPCS code, from its newest taxonomy file. Codes and CMS's category names only, no code descriptors.</item>
/// </list>
/// Both sources are public domain. Each file gets a .source.json next to it naming the exact download.
/// </summary>
internal static partial class CodeSetsBuilder
{
    private const string HcpcsPage = "https://www.cms.gov/medicare/coding-billing/healthcare-common-procedure-system/quarterly-update";
    private const string CmsCatalog = "https://data.cms.gov/data.json";
    private const string RbcsTitle = "Restructured BETOS Classification System";

    public static async Task RunAsync(string outputDirectory, string cache, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(cache);
        Directory.CreateDirectory(outputDirectory);
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; PriceTransparencyExplorer-ReferenceBuilder/1.0)");

        // The quarterly page lists the newest file first.
        var hcpcsUrl = await ProviderDirectoryBuilder.FindLinkAsync(http, HcpcsPage, HcpcsZip(), "https://www.cms.gov", cancellationToken);
        var hcpcsZip = await ProviderDirectoryBuilder.DownloadAsync(http, hcpcsUrl, cache, cancellationToken);
        var names = ReadHcpcsNames(hcpcsZip);
        var hcpcsOutput = Path.Combine(outputDirectory, "hcpcs_names.csv.gz");
        WriteCsvGz(hcpcsOutput, "code,description", names.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key},{CsvLine.Quote(pair.Value)}"));
        await WriteSourceAsync(hcpcsOutput, new
        {
            generatedAt = DateTimeOffset.UtcNow,
            source = hcpcsUrl,
            release = ReleaseOf(hcpcsUrl),
            codes = names.Count,
        }, cancellationToken);
        Console.WriteLine($"HCPCS Level II names: {names.Count:N0} codes → {hcpcsOutput}");

        var rbcsUrl = await FindRbcsCsvAsync(http, cancellationToken);
        var rbcsCsv = await ProviderDirectoryBuilder.DownloadAsync(http, rbcsUrl, cache, cancellationToken);
        var classes = ReadRbcs(rbcsCsv);
        var rbcsOutput = Path.Combine(outputDirectory, "rbcs.csv.gz");
        WriteCsvGz(rbcsOutput, "code,rbcs_id,category_code,category,subcategory_code,subcategory,family_code,family",
            classes.OrderBy(row => row.Code, StringComparer.Ordinal).Select(row => string.Join(',',
                row.Code, row.RbcsId, row.CategoryCode, CsvLine.Quote(row.Category), row.SubcategoryCode, CsvLine.Quote(row.Subcategory),
                row.FamilyCode, CsvLine.Quote(row.Family))));
        await WriteSourceAsync(rbcsOutput, new
        {
            generatedAt = DateTimeOffset.UtcNow,
            source = rbcsUrl,
            release = RbcsRelease().Match(rbcsUrl) is { Success: true } year ? $"RY20{year.Groups[1].Value}" : null,
            codes = classes.Count,
        }, cancellationToken);
        Console.WriteLine($"RBCS classes: {classes.Count:N0} codes → {rbcsOutput}");
    }

    /// <summary>
    /// The quarterly file's fixed-width text table: record type 3 starts a code (its long name in columns 12–91), type 4
    /// lines continue the name. Codes are 5 characters, a letter and 4 digits; dental D codes are skipped.
    /// </summary>
    internal static Dictionary<string, string> ReadHcpcsNames(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.FirstOrDefault(e => HcpcsTable().IsMatch(e.Name))
            ?? throw new InvalidDataException($"No HCPCS alpha-numeric text table in {Path.GetFileName(zipPath)}.");
        using var reader = new StreamReader(entry.Open(), Encoding.Latin1);
        return ReadHcpcsNames(reader);
    }

    internal static Dictionary<string, string> ReadHcpcsNames(TextReader reader)
    {
        var parts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            // Continuation lines end where their text does, so they're often shorter than the full record.
            if (line.Length < 12 || line[10] is not ('3' or '4'))
            {
                continue;
            }

            var code = line[..5];
            if (!LevelTwoCode().IsMatch(code) || code[0] == 'D')
            {
                continue;
            }

            var text = line[11..Math.Min(line.Length, 91)].Trim();
            if (line[10] == '3' || !parts.ContainsKey(code))
            {
                parts[code] = [];
            }

            if (text.Length > 0)
            {
                parts[code].Add(text);
            }
        }

        return parts
            .Where(pair => pair.Value.Count > 0)
            .ToDictionary(pair => pair.Key, pair => Spaces().Replace(string.Join(' ', pair.Value), " "), StringComparer.Ordinal);
    }

    /// <summary>One row per code: the RBCS assignment CMS marks as the latest. Family "000" means none.</summary>
    internal static List<RbcsRow> ReadRbcs(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8);
        var header = CsvLine.Split(reader.ReadLine() ?? "");
        int Column(string name) => header.IndexOf(name) is >= 0 and var index
            ? index
            : throw new InvalidDataException($"The RBCS file has no {name} column.");

        var (code, id, category, categoryName, subcategory, subcategoryName, family, familyName, latest) = (
            Column("HCPCS_Cd"), Column("RBCS_Id"), Column("RBCS_Cat"), Column("RBCS_Cat_Desc"), Column("RBCS_Cat_Subcat"),
            Column("RBCS_Subcat_Desc"), Column("RBCS_FamNumb"), Column("RBCS_Family_Desc"), Column("RBCS_Latest_Assignment"));
        var rows = new Dictionary<string, RbcsRow>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var fields = CsvLine.Split(line);
            if (fields.Count < header.Count || fields[latest] != "1")
            {
                continue;
            }

            var noFamily = fields[family] is "" or "000";
            rows[fields[code]] = new RbcsRow(
                fields[code], fields[id], fields[category], fields[categoryName], fields[subcategory], fields[subcategoryName],
                noFamily ? "" : fields[family], noFamily ? "" : fields[familyName]);
        }

        return [.. rows.Values];
    }

    /// <summary>The RBCS taxonomy's CSV download, from CMS's data catalog.</summary>
    private static async Task<string> FindRbcsCsvAsync(HttpClient http, CancellationToken cancellationToken)
    {
        await using var catalog = await http.GetStreamAsync(CmsCatalog, cancellationToken);
        using var document = await JsonDocument.ParseAsync(catalog, cancellationToken: cancellationToken);
        foreach (var dataset in document.RootElement.GetProperty("dataset").EnumerateArray())
        {
            if (dataset.GetProperty("title").GetString()?.StartsWith(RbcsTitle, StringComparison.Ordinal) != true)
            {
                continue;
            }

            foreach (var distribution in dataset.GetProperty("distribution").EnumerateArray())
            {
                if (distribution.TryGetProperty("format", out var format) && format.GetString() == "CSV"
                    && distribution.TryGetProperty("downloadURL", out var url))
                {
                    return url.GetString()!;
                }
            }
        }

        throw new InvalidDataException($"No CSV download for \"{RbcsTitle}\" in {CmsCatalog}.");
    }

    private static void WriteCsvGz(string path, string header, IEnumerable<string> lines)
    {
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(gzip, new UTF8Encoding(false)) { NewLine = "\n" };
        writer.WriteLine(header);
        foreach (var line in lines)
        {
            writer.WriteLine(line);
        }
    }

    private static Task WriteSourceAsync(string output, object source, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(Path.ChangeExtension(Path.ChangeExtension(output, null), ".source.json"),
            JsonSerializer.Serialize(source, new JsonSerializerOptions { WriteIndented = true }) + "\n", cancellationToken);

    /// <summary>".../october-2026-alpha-numeric-hcpcs-file.zip" → "October 2026".</summary>
    private static string? ReleaseOf(string url) =>
        HcpcsZip().Match(url) is { Success: true } match && match.Groups["month"].Value is { Length: > 0 } month
            ? $"{char.ToUpperInvariant(month[0])}{month[1..]} {match.Groups["year"].Value}"
            : null;

    [GeneratedRegex(@"(/files/zip/(?<month>[a-z]+)-(?<year>\d{4})-alpha-numeric-hcpcs-files?\.zip)", RegexOptions.IgnoreCase)]
    private static partial Regex HcpcsZip();

    [GeneratedRegex(@"^HCPC\d{4}_[A-Z]{3}_ANWEB.*\.txt$", RegexOptions.IgnoreCase)]
    private static partial Regex HcpcsTable();

    [GeneratedRegex(@"^[A-Z]\d{4}$")]
    private static partial Regex LevelTwoCode();

    [GeneratedRegex(@"_RY(\d{2})_", RegexOptions.IgnoreCase)]
    private static partial Regex RbcsRelease();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();
}

internal sealed record RbcsRow(
    string Code, string RbcsId, string CategoryCode, string Category, string SubcategoryCode, string Subcategory, string FamilyCode, string Family);
