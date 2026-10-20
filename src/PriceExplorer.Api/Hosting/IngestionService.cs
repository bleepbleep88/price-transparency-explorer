using Microsoft.Extensions.Options;
using PriceExplorer.Api.Configuration;
using PriceExplorer.Api.Core.Ingestion;

namespace PriceExplorer.Api.Hosting;

/// <summary>Loads rate data in the background so the API (and its live status) is reachable immediately.</summary>
internal sealed class IngestionService(
    IngestionCoordinator coordinator,
    DuckDbIngestionWriter writer,
    DataPaths paths,
    IngestionStatus status,
    IOptions<IngestionOptions> options,
    ILogger<IngestionService> logger) : BackgroundService
{
    private bool _storageReady;

    /// <summary>
    /// Hosted services start before the web server, so preparing storage here means no request ever meets a missing
    /// table. A storage problem is reported in the status instead of stopping the app.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            paths.EnsureCreated();
            await writer.EnsureSchemaAsync(cancellationToken);
            _storageReady = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Can't open the data directory {Root}", paths.Root);
            status.Update(s => s with { Phase = IngestionPhase.Failed, Message = $"Can't open the data directory {paths.Root}: {ex.Message}" });
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_storageReady)
        {
            return;
        }

        try
        {
            if (!options.Value.Enabled)
            {
                await ServeSavedDataOnlyAsync(stoppingToken);
                return;
            }

            await coordinator.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            // Never let a loading failure stop the host: the API stays up and reports the problem.
            logger.LogError(ex, "Ingestion failed");
            status.Update(s => s with { Phase = IngestionPhase.Failed, Message = $"Loading data failed: {ex.Message}" });
        }
    }

    private async Task ServeSavedDataOnlyAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Ingestion is disabled by configuration; serving saved data only");
        if (!await writer.HasLoadedDataAsync(cancellationToken))
        {
            status.Update(s => s with { Phase = IngestionPhase.Failed, Message = "Loading is turned off and there is no saved data." });
            return;
        }

        // Saved rates are searchable at once. Statistics a stopped run never finished are built from them meanwhile.
        status.Update(s => s with { DataAvailable = true, Message = "Loading is turned off; showing saved data." });
        await coordinator.BuildStatisticsAsync(cancellationToken);
        status.Update(s => s with { Phase = IngestionPhase.Ready, Message = "Loading is turned off; showing saved data." });
    }
}
