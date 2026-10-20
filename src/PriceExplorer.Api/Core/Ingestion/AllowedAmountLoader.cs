using System.Diagnostics;

namespace PriceExplorer.Api.Core.Ingestion;

/// <summary>
/// Loads one out-of-network allowed-amount file the way rate files load: through the download cache (ETag re-check,
/// resume, cached copy on an outage), skipped when its bytes and the parser are unchanged, parsed as a stream, saved in
/// one transaction. These files are extras: a failure is reported as a warning and never changes what search shows.
/// </summary>
internal sealed class AllowedAmountLoader(
    FileDownloader downloader,
    DuckDbIngestionWriter files,
    AllowedAmountWriter writer,
    IngestionStatus status,
    ILogger<AllowedAmountLoader> logger)
{
    public async Task LoadAsync(int snapshotId, TableOfContents index, AllowedAmountFileRef file, CancellationToken cancellationToken)
    {
        var label = $"{file.ProductLabel} out-of-network amounts (snapshot {index.Header.IndexDate:yyyy-MM-dd})";
        SourceFileState? state = null;
        try
        {
            state = await files.UpsertSourceFileAsync(
                snapshotId,
                new SourceFileSpec(SourceKinds.AllowedAmount, file.StructureIndex, file.ProductKey, file.ProductLabel, file.Url, file.Description),
                cancellationToken);

            // These files are small (under 1 MB), so the status shows the step without byte progress.
            status.Update(s => s with { Phase = IngestionPhase.Downloading, Message = $"Downloading {label}…", CurrentFile = label, CurrentFileProgress = null });
            var download = await downloader.FetchAsync(file.Url, onProgress: null, cancellationToken);
            if (download.Warning is not null)
            {
                status.AddIssue("warning", $"{label}: {download.Warning}", file.Url);
            }

            if (state.Status is "loaded" or "partial" && state.Sha256 == download.Sha256 && state.ParserVersion == AllowedAmountWriter.ParserVersion)
            {
                await files.MarkFileUnchangedAsync(state.FileId, download, cancellationToken);
                logger.LogInformation("Ingestion: {File} unchanged since last load (sha256 {Sha})", label, download.Sha256[..12]);
                return;
            }

            status.Update(s => s with { Phase = IngestionPhase.Parsing, Message = $"Reading {label}…" });
            var builder = new AllowedAmountFileBuilder();
            var stopwatch = Stopwatch.StartNew();
            var outcome = await AllowedAmountFileParser.ParseAsync(download.LocalPath, builder, onBytesRead: null, cancellationToken);
            stopwatch.Stop();

            status.Update(s => s with { Phase = IngestionPhase.Saving, Message = $"Saving {label}…" });
            await writer.WriteAsync(state.FileId, builder, download, outcome, stopwatch.Elapsed, cancellationToken);

            var stats = builder.GetStats();
            logger.LogInformation(
                "Ingestion: {File}: {Items} items, {Published} published payments → {Unique} unique ({Duplicates} duplicates collapsed, {Rejected} rejected), {Eins} EINs, {Npis} NPIs, parsed in {ParseMs} ms",
                label, stats.Items, stats.PaymentsRaw, stats.PaymentsUnique, stats.DuplicatesRemoved, stats.RejectedPayments,
                stats.DistinctEins, stats.DistinctNpis, (int)stopwatch.Elapsed.TotalMilliseconds);

            if (!outcome.IsComplete)
            {
                status.AddIssue("warning", $"{label} was only partly read. {outcome.Problem}", file.Url);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Ingestion: {File} failed", label);
            if (state is not null)
            {
                await files.MarkFileFailedAsync(state.FileId, ex.Message, cancellationToken);
            }

            status.AddIssue("warning", state?.Status is "loaded" or "partial"
                ? $"{label}: refresh failed, still showing the last good copy. {ex.Message}"
                : $"{label}: couldn't be loaded; search isn't affected. {ex.Message}", file.Url);
        }
    }
}
