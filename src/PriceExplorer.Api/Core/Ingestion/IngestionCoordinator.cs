using System.Diagnostics;
using Microsoft.Extensions.Options;
using PriceExplorer.Api.Configuration;

namespace PriceExplorer.Api.Core.Ingestion;

/// <summary>
/// Runs the whole load: read each configured index, then download → parse → save every in-network file it lists.
/// One file failing never stops the others, and data from earlier runs stays queryable throughout.
/// </summary>
internal sealed class IngestionCoordinator(
    IOptions<IngestionOptions> options,
    TableOfContentsReader indexReader,
    FileDownloader downloader,
    DuckDbIngestionWriter writer,
    IngestionStatus status,
    ILogger<IngestionCoordinator> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await writer.EnsureSchemaAsync(cancellationToken);

        if (await writer.HasLoadedDataAsync(cancellationToken))
        {
            status.Update(s => s with { DataAvailable = true, Message = "Showing saved data while checking the payer for updates…" });
        }

        var indexes = new List<TableOfContents>();
        foreach (var source in options.Value.Snapshots)
        {
            status.Update(s => s with { Phase = IngestionPhase.Downloading, Message = $"Reading index {ShortName(source.IndexUrl)}…" });
            try
            {
                var index = await indexReader.ReadAsync(source.IndexUrl, cancellationToken);
                if (index.Warning is not null)
                {
                    status.AddIssue("warning", index.Warning, source.IndexUrl);
                }

                indexes.Add(index);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Ingestion: couldn't read index {IndexUrl}", source.IndexUrl);
                status.AddIssue("error", $"Couldn't read index {ShortName(source.IndexUrl)}: {ex.Message}", source.IndexUrl);
            }
        }

        status.Update(s => s with { FilesTotal = indexes.Sum(index => index.InNetworkFiles.Count), FilesDone = 0 });

        foreach (var index in indexes)
        {
            // Each snapshot is isolated: a failure here is reported and the next snapshot still loads.
            int? snapshotId = null;
            string? snapshotError = "Loading stopped before every file was read; it continues on the next start.";
            try
            {
                snapshotId = await writer.UpsertSnapshotAsync(index.IndexUrl, index.Header, cancellationToken);
                await writer.ReplacePlansAsync(snapshotId.Value, index.Plans, index.StructureFiles, cancellationToken);

                foreach (var file in index.InNetworkFiles)
                {
                    var available = await LoadFileAsync(snapshotId.Value, index, file, cancellationToken);
                    status.Update(s => s with { FilesDone = s.FilesDone + 1, DataAvailable = s.DataAvailable || available });
                }

                snapshotError = null;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Ingestion: snapshot {IndexUrl} failed", index.IndexUrl);
                status.AddIssue("error", $"Snapshot {ShortName(index.IndexUrl)} couldn't be saved: {ex.Message}", index.IndexUrl);
                snapshotError = ex.Message;
            }
            finally
            {
                // Always settle the snapshot (ready, partial or failed) so it never stays "loading" after a stop or an error.
                if (snapshotId is { } id)
                {
                    await CompleteSnapshotAsync(id, index, snapshotError);
                }
            }
        }

        status.Update(s => s.DataAvailable
            ? s with { Phase = IngestionPhase.Ready, Message = "Data is ready.", CurrentFile = null, CurrentFileProgress = null }
            : s with { Phase = IngestionPhase.Failed, Message = "No rate data could be loaded. See the issues listed below.", CurrentFile = null, CurrentFileProgress = null });
    }

    private async Task CompleteSnapshotAsync(int snapshotId, TableOfContents index, string? error)
    {
        try
        {
            // Not cancellable: this runs during shutdown too, and it's one small UPDATE.
            var snapshotStatus = await writer.CompleteSnapshotAsync(snapshotId, error, CancellationToken.None);
            logger.LogInformation("Ingestion: snapshot {IndexDate} is {Status}", index.Header.IndexDate, snapshotStatus);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ingestion: couldn't record the status of snapshot {IndexUrl}", index.IndexUrl);
        }
    }

    /// <returns>True when this file has queryable data afterwards (fresh, unchanged, partial, or from an earlier run).</returns>
    private async Task<bool> LoadFileAsync(int snapshotId, TableOfContents index, InNetworkFileRef file, CancellationToken cancellationToken)
    {
        var spec = new SourceFileSpec("in_network", file.StructureIndex, file.ProductKey, file.ProductLabel, file.Url, file.Description);
        var state = await writer.UpsertSourceFileAsync(snapshotId, spec, cancellationToken);
        var label = $"{file.ProductLabel} rates (snapshot {index.Header.IndexDate:yyyy-MM-dd})";
        var progress = new ProgressThrottle();

        try
        {
            status.Update(s => s with { Phase = IngestionPhase.Downloading, Message = $"Downloading {label}…", CurrentFile = label, CurrentFileProgress = 0 });
            var download = await downloader.FetchAsync(file.Url, (done, total) => Report(progress, IngestionPhase.Downloading, "Downloading", label, done, total), cancellationToken);
            if (download.Warning is not null)
            {
                status.AddIssue("warning", $"{label}: {download.Warning}", file.Url);
            }

            if (state.Status is "loaded" or "partial" && state.Sha256 == download.Sha256 && state.ParserVersion == DuckDbIngestionWriter.ParserVersion)
            {
                await writer.MarkFileUnchangedAsync(state.FileId, download, cancellationToken);
                logger.LogInformation("Ingestion: {File} unchanged since last load (sha256 {Sha})", label, download.Sha256[..12]);
                return true;
            }

            status.Update(s => s with { Phase = IngestionPhase.Parsing, Message = $"Reading {label}…", CurrentFileProgress = 0 });
            var builder = new InNetworkFileBuilder();
            var stopwatch = Stopwatch.StartNew();
            var outcome = await InNetworkFileParser.ParseAsync(
                download.LocalPath, builder, bytes => Report(progress, IngestionPhase.Parsing, "Reading", label, bytes, download.Length), cancellationToken);
            stopwatch.Stop();

            status.Update(s => s with { Phase = IngestionPhase.Saving, Message = $"Saving {label}…", CurrentFileProgress = null });
            await writer.WriteInNetworkFileAsync(state.FileId, builder, download, outcome, stopwatch.Elapsed, cancellationToken);

            var stats = builder.GetStats();
            logger.LogInformation(
                "Ingestion: {File}: {Items} items, {Prices} published prices → {Unique} unique rates ({Duplicates} duplicates collapsed, {Rejected} rejected), {Eins} EINs, {Npis} NPIs, parsed in {ParseMs} ms",
                label, stats.Items, stats.PricesRaw, stats.RatesUnique, stats.DuplicatesRemoved, stats.RejectedPrices,
                stats.DistinctEins, stats.DistinctNpis, (int)stopwatch.Elapsed.TotalMilliseconds);

            if (!outcome.IsComplete)
            {
                status.AddIssue("warning", $"{label} was only partly read. {outcome.Problem}", file.Url);
            }

            return true;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Ingestion: {File} failed", label);
            await writer.MarkFileFailedAsync(state.FileId, ex.Message, cancellationToken);
            var keptOldData = state.Status is "loaded" or "partial";
            status.AddIssue(keptOldData ? "warning" : "error", keptOldData
                ? $"{label}: refresh failed, still showing the last good copy. {ex.Message}"
                : $"{label}: couldn't be loaded. {ex.Message}", file.Url);
            return keptOldData;
        }
    }

    private void Report(ProgressThrottle throttle, IngestionPhase phase, string verb, string label, long done, long? total)
    {
        if (total is not > 0 || !throttle.ShouldReport(done, total.Value))
        {
            return;
        }

        var fraction = Math.Min(1d, (double)done / total.Value);
        status.Update(s => s with
        {
            Phase = phase,
            CurrentFileProgress = Math.Round(fraction, 3),
            Message = $"{verb} {label}: {done / 1_048_576d:N0} of {total.Value / 1_048_576d:N0} MB",
        });
    }

    private static string ShortName(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? Path.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath)) : url;

    /// <summary>Publishes progress at most once per whole percent, so the status stream stays light.</summary>
    private sealed class ProgressThrottle
    {
        private int _lastPercent = -1;

        public bool ShouldReport(long done, long total)
        {
            var percent = (int)(done * 100 / total);
            if (percent == _lastPercent)
            {
                return false;
            }

            _lastPercent = percent;
            return true;
        }
    }
}
