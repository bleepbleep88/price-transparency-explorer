using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using DuckDB.NET.Data;

namespace PriceExplorer.ReferenceBuilder;

/// <summary>
/// Builds reference/provider_directory.csv.gz: for each NPI in the loaded rate files, its name, NUCC specialty,
/// practice location and county (NYC boroughs named). Sources are public CMS/NUCC/Census files; street addresses and
/// phone numbers are deliberately left out.
/// </summary>
internal static partial class ProviderDirectoryBuilder
{
    private const string NppesPage = "https://download.cms.gov/nppes/NPI_Files.html";
    private const string NuccPage = "https://www.nucc.org/index.php/code-sets-mainmenu-41/provider-taxonomy-mainmenu-40/csv-mainmenu-57";
    private const string CensusZipToCounty = "https://www2.census.gov/geo/docs/maps-data/data/rel2020/zcta520/tab20_zcta520_county20_natl.txt";

    private static readonly Dictionary<string, string> NycBoroughs = new(StringComparer.Ordinal)
    {
        ["36005"] = "Bronx",
        ["36047"] = "Brooklyn",
        ["36061"] = "Manhattan",
        ["36081"] = "Queens",
        ["36085"] = "Staten Island",
    };

    public static async Task RunAsync(string database, string output, string cache, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(cache);
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PriceTransparencyExplorer-ReferenceBuilder/1.0");

        var npis = await LoadNpisAsync(database);
        Console.WriteLine($"NPIs in the loaded rate files: {npis.Count:N0}");

        var nppesUrl = await FindLinkAsync(http, NppesPage, NppesMonthlyZip(), "https://download.cms.gov/nppes/", cancellationToken);
        var nuccUrl = await FindLinkAsync(http, NuccPage, NuccCsv(), "https://www.nucc.org", cancellationToken);

        var nppesZip = await DownloadAsync(http, nppesUrl, cache, cancellationToken);
        var nuccCsv = await DownloadAsync(http, nuccUrl, cache, cancellationToken);
        var zipCounties = await DownloadAsync(http, CensusZipToCounty, cache, cancellationToken);

        var taxonomy = ReadTaxonomy(nuccCsv);
        var counties = ReadZipCounties(zipCounties);
        Console.WriteLine($"NUCC taxonomy codes: {taxonomy.Count:N0}; ZIP codes mapped to counties: {counties.Count:N0}");

        var providers = FillCountiesFromCities(ReadNppes(nppesZip, npis, taxonomy, counties));
        Console.WriteLine($"Matched {providers.Count:N0} of {npis.Count:N0} NPIs ({providers.Count * 100.0 / Math.Max(1, npis.Count):F1}%)");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        WriteCsvGz(output, providers);
        await File.WriteAllTextAsync(Path.ChangeExtension(Path.ChangeExtension(output, null), ".source.json"), JsonSerializer.Serialize(new
        {
            generatedAt = DateTimeOffset.UtcNow,
            nppes = nppesUrl,
            nucc = nuccUrl,
            census = CensusZipToCounty,
            npisRequested = npis.Count,
            npisFound = providers.Count,
        }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        Console.WriteLine($"Wrote {output}");
    }

    private static async Task<HashSet<long>> LoadNpisAsync(string database)
    {
        using var connection = new DuckDBConnection($"Data Source={database};ACCESS_MODE=READ_ONLY");
        await connection.OpenAsync();
        return [.. await connection.QueryAsync<long>("SELECT DISTINCT npi FROM group_npis")];
    }

    private static async Task<string> FindLinkAsync(HttpClient http, string page, Regex pattern, string baseUrl, CancellationToken cancellationToken)
    {
        var html = await http.GetStringAsync(page, cancellationToken);
        var match = pattern.Match(html);
        if (!match.Success)
        {
            throw new InvalidDataException($"No download link found on {page}.");
        }

        return new Uri(new Uri(baseUrl), match.Groups[1].Value.TrimStart('.', '/')).ToString();
    }

    /// <summary>Downloads into the cache once (resuming a partial file); later runs reuse it when the size still matches.</summary>
    private static async Task<string> DownloadAsync(HttpClient http, string url, string cache, CancellationToken cancellationToken)
    {
        var path = Path.Combine(cache, Path.GetFileName(new Uri(url).AbsolutePath));
        using var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), cancellationToken);
        head.EnsureSuccessStatusCode();
        var length = head.Content.Headers.ContentLength;
        if (File.Exists(path) && new FileInfo(path).Length == length)
        {
            Console.WriteLine($"Cached: {Path.GetFileName(path)}");
            return path;
        }

        var partial = path + ".partial";
        var existing = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var append = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
        await using (var body = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var file = new FileStream(partial, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
        {
            var buffer = new byte[1 << 20];
            long written = append ? existing : 0;
            var lastPercent = -1;
            int read;
            while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;
                if (length is > 0 && (int)(written * 100 / length.Value) is var percent && percent != lastPercent && percent % 10 == 0)
                {
                    lastPercent = percent;
                    Console.WriteLine($"  {Path.GetFileName(path)}: {percent}%");
                }
            }
        }

        File.Move(partial, path, overwrite: true);
        return path;
    }

    /// <summary>NUCC taxonomy: code → display name and grouping.</summary>
    private static Dictionary<string, (string Name, string Group)> ReadTaxonomy(string path)
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StreamReader(path, Encoding.Latin1);
        var header = CsvLine.Split(reader.ReadLine() ?? "");
        int Column(string name) => header.FindIndex(h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
        var (code, grouping, classification, specialization, display) =
            (Column("Code"), Column("Grouping"), Column("Classification"), Column("Specialization"), Column("Display Name"));

        while (reader.ReadLine() is { } line)
        {
            var fields = CsvLine.Split(line);
            if (fields.Count <= Math.Max(code, classification))
            {
                continue;
            }

            var name = display >= 0 && display < fields.Count && fields[display].Length > 0
                ? fields[display]
                : specialization >= 0 && fields[specialization].Length > 0
                    ? $"{fields[classification]} ({fields[specialization]})"
                    : fields[classification];
            result[fields[code]] = (name, grouping >= 0 ? fields[grouping] : "");
        }

        return result;
    }

    /// <summary>Census 2020 ZCTA → county, picking the county holding most of each ZIP's land area.</summary>
    private static Dictionary<string, (string Fips, string Name)> ReadZipCounties(string path)
    {
        var best = new Dictionary<string, (string Fips, string Name, long Land)>(StringComparer.Ordinal);
        using var reader = new StreamReader(path, Encoding.UTF8);
        var header = (reader.ReadLine() ?? "").TrimStart('\uFEFF').Split('|');
        int Column(string name) => Array.FindIndex(header, h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
        var (zip, countyFips, countyName, landPart) = (Column("GEOID_ZCTA5_20"), Column("GEOID_COUNTY_20"), Column("NAMELSAD_COUNTY_20"), Column("AREALAND_PART"));

        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split('|');
            if (fields.Length <= landPart || fields[zip].Length != 5)
            {
                continue;
            }

            var land = long.TryParse(fields[landPart], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
            if (!best.TryGetValue(fields[zip], out var current) || land > current.Land)
            {
                best[fields[zip]] = (fields[countyFips], fields[countyName], land);
            }
        }

        return best.ToDictionary(pair => pair.Key, pair => (pair.Value.Fips, pair.Value.Name), StringComparer.Ordinal);
    }

    /// <summary>Streams npidata_pfile_*.csv out of the NPPES zip, fully parsing only lines whose NPI we need.</summary>
    private static List<ProviderRecord> ReadNppes(
        string zipPath, HashSet<long> npis, Dictionary<string, (string Name, string Group)> taxonomy, Dictionary<string, (string Fips, string Name)> counties)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.FirstOrDefault(e => NpiDataFile().IsMatch(e.Name))
            ?? throw new InvalidDataException("The NPPES zip has no npidata_pfile CSV.");
        Console.WriteLine($"Reading {entry.Name} ({entry.Length / 1_073_741_824d:F1} GB uncompressed)…");

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 20);
        var header = CsvLine.Split(reader.ReadLine() ?? "");
        int Column(string name) => header.FindIndex(h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
        var columns = new
        {
            Npi = Column("NPI"),
            EntityType = Column("Entity Type Code"),
            Organization = Column("Provider Organization Name (Legal Business Name)"),
            Last = Column("Provider Last Name (Legal Name)"),
            First = Column("Provider First Name"),
            Credential = Column("Provider Credential Text"),
            City = Column("Provider Business Practice Location Address City Name"),
            State = Column("Provider Business Practice Location Address State Name"),
            Postal = Column("Provider Business Practice Location Address Postal Code"),
            Country = Column("Provider Business Practice Location Address Country Code (If outside U.S.)"),
            Deactivated = Column("NPI Deactivation Date"),
            Reactivated = Column("NPI Reactivation Date"),
            Updated = Column("Last Update Date"),
            Taxonomies = Enumerable.Range(1, 15).Select(i => (Code: Column($"Healthcare Provider Taxonomy Code_{i}"), Primary: Column($"Healthcare Provider Primary Taxonomy Switch_{i}"))).ToArray(),
        };

        var result = new List<ProviderRecord>(npis.Count);
        long lines = 0;
        while (reader.ReadLine() is { } line)
        {
            if (++lines % 1_000_000 == 0)
            {
                Console.WriteLine($"  {lines:N0} registry rows read, {result.Count:N0} matched");
            }

            if (!TryReadLeadingNpi(line, out var npi) || !npis.Contains(npi))
            {
                continue;
            }

            var fields = CsvLine.Split(line);
            string Field(int index) => index >= 0 && index < fields.Count ? fields[index].Trim() : "";

            var taxonomyCode = columns.Taxonomies.Where(t => Field(t.Primary) == "Y").Select(t => Field(t.Code)).FirstOrDefault(code => code.Length > 0)
                ?? columns.Taxonomies.Select(t => Field(t.Code)).FirstOrDefault(code => code.Length > 0)
                ?? "";
            var (specialty, group) = taxonomy.TryGetValue(taxonomyCode, out var found) ? found : ("", "");
            var zip5 = Field(columns.Postal) is { Length: >= 5 } postal ? postal[..5] : "";
            // A practice outside the US has a foreign postal code, which may look like a US ZIP (Rota, Spain: 11530).
            var abroad = Field(columns.Country) is { Length: > 0 } country && country != "US";
            var (countyFips, countyName) = !abroad && counties.TryGetValue(zip5, out var county) ? county : ("", "");
            // Deactivated NPIs keep only their number and dates in the registry, so their entity type is blank.
            var entityType = Field(columns.EntityType) switch
            {
                "1" => "individual",
                "2" => "organization",
                _ => "",
            };
            var organization = entityType == "organization";
            var name = organization
                ? Field(columns.Organization)
                : string.Join(" ", new[] { Field(columns.First), Field(columns.Last) }.Where(part => part.Length > 0));

            result.Add(new ProviderRecord(
                npi,
                entityType,
                ToTitle(name),
                organization ? "" : Field(columns.Credential),
                taxonomyCode,
                specialty,
                group,
                ToTitle(Field(columns.City)),
                Field(columns.State),
                zip5,
                countyFips,
                countyName,
                NycBoroughs.GetValueOrDefault(countyFips, ""),
                Field(columns.Deactivated).Length > 0 && Field(columns.Reactivated).Length == 0,
                Field(columns.Updated)));
        }

        Console.WriteLine($"  {lines:N0} registry rows read in total");
        return result;
    }

    /// <summary>
    /// The Census ZIP file has no unique or PO-box ZIPs (Roswell Park's 14263), and a ZIP from another state can't hold
    /// this address's county (Valhalla, NY at 22033). Such a record takes the county that 90% or more of the same city's
    /// other NPIs share, when one does. Otherwise it keeps the ZIP's county: "Liverpool, NJ" at 13090 is a state typo for
    /// Liverpool, NY, and the ZIP is right.
    /// </summary>
    private static List<ProviderRecord> FillCountiesFromCities(List<ProviderRecord> providers)
    {
        bool ZipFitsState(ProviderRecord p) =>
            p.CountyFips.Length == 5 && StateFips.TryGetValue(p.State, out var fips) && p.CountyFips.StartsWith(fips, StringComparison.Ordinal);

        var cityCounties = providers
            .Where(ZipFitsState)
            .GroupBy(p => (City: p.City.ToLowerInvariant(), p.State))
            .Select(city =>
            {
                var top = city.GroupBy(p => (p.CountyFips, p.County, p.Borough)).MaxBy(county => county.Count())!;
                return (city.Key, County: top.Key, Share: top.Count() / (double)city.Count());
            })
            .Where(city => city.Share >= 0.9)
            .ToDictionary(city => city.Key, city => city.County);

        return providers.Select(p =>
        {
            var misplaced = p.CountyFips.Length == 0 || (StateFips.ContainsKey(p.State) && !ZipFitsState(p));
            return misplaced && p.City.Length > 0 && cityCounties.TryGetValue((p.City.ToLowerInvariant(), p.State), out var county)
                ? p with { CountyFips = county.CountyFips, County = county.County, Borough = county.Borough }
                : p;
        }).ToList();
    }

    /// <summary>US states, DC and territories: USPS code to Census state FIPS code.</summary>
    private static readonly Dictionary<string, string> StateFips = new(StringComparer.Ordinal)
    {
        ["AL"] = "01", ["AK"] = "02", ["AZ"] = "04", ["AR"] = "05", ["CA"] = "06", ["CO"] = "08", ["CT"] = "09", ["DE"] = "10",
        ["DC"] = "11", ["FL"] = "12", ["GA"] = "13", ["HI"] = "15", ["ID"] = "16", ["IL"] = "17", ["IN"] = "18", ["IA"] = "19",
        ["KS"] = "20", ["KY"] = "21", ["LA"] = "22", ["ME"] = "23", ["MD"] = "24", ["MA"] = "25", ["MI"] = "26", ["MN"] = "27",
        ["MS"] = "28", ["MO"] = "29", ["MT"] = "30", ["NE"] = "31", ["NV"] = "32", ["NH"] = "33", ["NJ"] = "34", ["NM"] = "35",
        ["NY"] = "36", ["NC"] = "37", ["ND"] = "38", ["OH"] = "39", ["OK"] = "40", ["OR"] = "41", ["PA"] = "42", ["RI"] = "44",
        ["SC"] = "45", ["SD"] = "46", ["TN"] = "47", ["TX"] = "48", ["UT"] = "49", ["VT"] = "50", ["VA"] = "51", ["WA"] = "53",
        ["WV"] = "54", ["WI"] = "55", ["WY"] = "56", ["AS"] = "60", ["GU"] = "66", ["MP"] = "69", ["PR"] = "72", ["VI"] = "78",
    };

    private static bool TryReadLeadingNpi(string line, out long npi)
    {
        // Each row starts with "NNNNNNNNNN", (quoted 10-digit NPI).
        npi = 0;
        return line.Length > 12 && line[0] == '"' && line[11] == '"'
            && long.TryParse(line.AsSpan(1, 10), NumberStyles.None, CultureInfo.InvariantCulture, out npi);
    }

    private static string ToTitle(string value) =>
        value.Length == 0 ? value : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());

    private static void WriteCsvGz(string path, IEnumerable<ProviderRecord> providers)
    {
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(gzip, new UTF8Encoding(false)) { NewLine = "\n" };
        writer.WriteLine("npi,entity_type,name,credential,taxonomy_code,specialty,specialty_group,city,state,zip5,county_fips,county,borough,deactivated,nppes_last_updated");
        foreach (var p in providers.OrderBy(p => p.Npi))
        {
            writer.WriteLine(string.Join(',',
                p.Npi.ToString(CultureInfo.InvariantCulture), p.EntityType, CsvLine.Quote(p.Name), CsvLine.Quote(p.Credential),
                p.TaxonomyCode, CsvLine.Quote(p.Specialty), CsvLine.Quote(p.SpecialtyGroup), CsvLine.Quote(p.City), p.State, p.Zip5,
                p.CountyFips, CsvLine.Quote(p.County), CsvLine.Quote(p.Borough), p.Deactivated ? "true" : "false", p.LastUpdated));
        }
    }

    [GeneratedRegex(@"href=['""](\.?/?NPPES_Data_Dissemination_[A-Za-z]+_\d{4}_V2\.zip)['""]", RegexOptions.IgnoreCase)]
    private static partial Regex NppesMonthlyZip();

    [GeneratedRegex(@"href=['""](/images/stories/CSV/nucc_taxonomy_\d+\.csv)['""]", RegexOptions.IgnoreCase)]
    private static partial Regex NuccCsv();

    [GeneratedRegex(@"^npidata_pfile_\d+-\d+\.csv$", RegexOptions.IgnoreCase)]
    private static partial Regex NpiDataFile();
}

internal sealed record ProviderRecord(
    long Npi,
    string EntityType,
    string Name,
    string Credential,
    string TaxonomyCode,
    string Specialty,
    string SpecialtyGroup,
    string City,
    string State,
    string Zip5,
    string CountyFips,
    string County,
    string Borough,
    bool Deactivated,
    string LastUpdated);

/// <summary>Minimal RFC 4180 field splitting (quoted fields, doubled quotes inside them).</summary>
internal static class CsvLine
{
    public static List<string> Split(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    public static string Quote(string value) =>
        value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
}
