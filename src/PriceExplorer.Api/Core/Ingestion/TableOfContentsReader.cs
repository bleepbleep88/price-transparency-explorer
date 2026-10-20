using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PriceExplorer.Api.Core.Ingestion.Tic;

namespace PriceExplorer.Api.Core.Ingestion;

/// <summary>Reads a table-of-contents (index) file: the plans it covers and which rate files to load for them.</summary>
internal sealed partial class TableOfContentsReader(FileDownloader downloader)
{
    public async Task<TableOfContents> ReadAsync(string indexUrl, CancellationToken cancellationToken)
    {
        var download = await downloader.FetchAsync(indexUrl, onProgress: null, cancellationToken);

        var options = TicJson.CreateOptions();
        TableOfContentsFile index;
        await using (var file = File.OpenRead(download.LocalPath))
        await using (var input = TicJson.OpenForReading(file))
        {
            index = await JsonSerializer.DeserializeAsync<TableOfContentsFile>(input, options, cancellationToken)
                ?? throw new InvalidDataException("The index file is empty.");
        }

        var structures = index.ReportingStructure ?? [];
        if (structures.Count == 0)
        {
            throw new InvalidDataException("The index lists no reporting structures, so there are no rate files to load.");
        }

        var plans = new List<PlanRecord>();
        var files = new List<InNetworkFileRef>();
        var allowedAmountFiles = new List<AllowedAmountFileRef>();
        var allowedAmountUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var structureFiles = new HashSet<StructureFileRecord>();
        var usedKeys = new HashSet<string>(StringComparer.Ordinal);
        var firstSpelling = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var structureIndex = 0; structureIndex < structures.Count; structureIndex++)
        {
            // Null elements are valid JSON with nothing in them; skip them.
            if (structures[structureIndex] is not { } structure)
            {
                continue;
            }

            var structurePlans = (structure.ReportingPlans ?? []).OfType<ReportingPlan>().ToList();
            plans.AddRange(structurePlans.Select(plan => new PlanRecord(
                structureIndex, plan.PlanName?.Trim(), plan.PlanIdType?.Trim(), plan.PlanId?.Trim(),
                plan.PlanMarketType?.Trim(), plan.IssuerName?.Trim(), plan.PlanSponsorName?.Trim())));

            var locations = (structure.InNetworkFiles ?? [])
                .OfType<FileLocation>()
                .Where(file => !string.IsNullOrWhiteSpace(file.Location))
                .ToList();

            var fallback = locations.Count > 0 ? FileStem(locations[0].Location!) : $"Plan group {structureIndex + 1}";
            var label = ProductNaming.LabelFor(structurePlans.Select(plan => plan.PlanName).ToList(), fallback);
            var key = ProductNaming.KeyFor(label);
            for (var suffix = 2; !usedKeys.Add(key); suffix++)
            {
                key = $"{ProductNaming.KeyFor(label)}-{suffix}";
            }

            foreach (var location in locations)
            {
                // A file shared by several structures is loaded once, but every structure keeps its link to it.
                var url = location.Location!.Trim();
                if (firstSpelling.TryAdd(url, url))
                {
                    files.Add(new InNetworkFileRef(structureIndex, key, label, url, location.Description?.Trim()));
                }

                structureFiles.Add(new StructureFileRecord(structureIndex, firstSpelling[url]));
            }

            // Out-of-network allowed amounts for the same plans: loaded once per file, under the structure's product.
            foreach (var location in AllowedAmountLocations(structure.AllowedAmountFile, options))
            {
                var url = location.Location!.Trim();
                if (allowedAmountUrls.Add(url))
                {
                    allowedAmountFiles.Add(new AllowedAmountFileRef(structureIndex, key, label, url, location.Description?.Trim()));
                }
            }
        }

        var header = new SnapshotHeader(
            IndexDate: DateFromUrl(indexUrl),
            LastUpdatedOn: InNetworkFileBuilder.ParseDate(index.LastUpdatedOn),
            ReportingEntityName: index.ReportingEntityName?.Trim(),
            ReportingEntityType: index.ReportingEntityType?.Trim(),
            SchemaVersion: index.Version?.Trim(),
            Sha256: download.Sha256);

        return new TableOfContents(indexUrl, header, plans, files, [.. structureFiles], allowedAmountFiles, download.Warning);
    }

    /// <summary>
    /// The schema allows one allowed_amount_file object per structure; an array of them is read too. Anything else (a
    /// string, an object without a location) is skipped rather than failing the whole index.
    /// </summary>
    private static IEnumerable<FileLocation> AllowedAmountLocations(JsonElement? value, JsonSerializerOptions options)
    {
        var elements = value switch
        {
            { ValueKind: JsonValueKind.Object } single => [single],
            { ValueKind: JsonValueKind.Array } list => list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).ToList(),
            _ => new List<JsonElement>(),
        };

        return elements
            .Select(element => element.Deserialize<FileLocation>(options))
            .OfType<FileLocation>()
            .Where(location => !string.IsNullOrWhiteSpace(location.Location));
    }

    /// <summary>CMS names index files "YYYY-MM-DD_..._index.json"; that date identifies the snapshot.</summary>
    internal static DateOnly? DateFromUrl(string url)
    {
        var match = DatePrefix().Match(Uri.UnescapeDataString(url));
        return match.Success && DateOnly.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static string FileStem(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? Path.GetFileNameWithoutExtension(uri.AbsolutePath) : url;

    [GeneratedRegex(@"(\d{4}-\d{2}-\d{2})_[^/]*$")]
    private static partial Regex DatePrefix();
}

internal sealed record TableOfContents(
    string IndexUrl,
    SnapshotHeader Header,
    IReadOnlyList<PlanRecord> Plans,
    IReadOnlyList<InNetworkFileRef> InNetworkFiles,
    IReadOnlyList<StructureFileRecord> StructureFiles,
    IReadOnlyList<AllowedAmountFileRef> AllowedAmountFiles,
    string? Warning);

/// <summary>One reporting structure's link to one rate file (the URL as stored on the file's source_files row).</summary>
internal sealed record StructureFileRecord(int StructureIndex, string Url);

internal sealed record InNetworkFileRef(int StructureIndex, string ProductKey, string ProductLabel, string Url, string? Description);

/// <summary>An out-of-network allowed-amount file, under the product of the first structure that lists it.</summary>
internal sealed record AllowedAmountFileRef(int StructureIndex, string ProductKey, string ProductLabel, string Url, string? Description);
