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

        TableOfContentsFile index;
        await using (var file = File.OpenRead(download.LocalPath))
        await using (var input = TicJson.OpenForReading(file))
        {
            index = await JsonSerializer.DeserializeAsync<TableOfContentsFile>(input, TicJson.CreateOptions(), cancellationToken)
                ?? throw new InvalidDataException("The index file is empty.");
        }

        var structures = index.ReportingStructure ?? [];
        if (structures.Count == 0)
        {
            throw new InvalidDataException("The index lists no reporting structures, so there are no rate files to load.");
        }

        var plans = new List<PlanRecord>();
        var files = new List<InNetworkFileRef>();
        var usedKeys = new HashSet<string>(StringComparer.Ordinal);
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var structureIndex = 0; structureIndex < structures.Count; structureIndex++)
        {
            var structure = structures[structureIndex];
            var structurePlans = structure.ReportingPlans ?? [];
            plans.AddRange(structurePlans.Select(plan => new PlanRecord(
                structureIndex, plan.PlanName?.Trim(), plan.PlanIdType?.Trim(), plan.PlanId?.Trim(),
                plan.PlanMarketType?.Trim(), plan.IssuerName?.Trim(), plan.PlanSponsorName?.Trim())));

            var locations = (structure.InNetworkFiles ?? [])
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
                var url = location.Location!.Trim();
                if (seenUrls.Add(url))
                {
                    files.Add(new InNetworkFileRef(structureIndex, key, label, url, location.Description?.Trim()));
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

        return new TableOfContents(indexUrl, header, plans, files, download.Warning);
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
    string? Warning);

internal sealed record InNetworkFileRef(int StructureIndex, string ProductKey, string ProductLabel, string Url, string? Description);
