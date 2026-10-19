using Microsoft.Extensions.Options;
using PriceExplorer.Api.Configuration;
using PriceExplorer.Api.Core.Ingestion;

namespace PriceExplorer.Api.Hosting;

/// <summary>Loads rate data in the background so the API (and its live status) is reachable immediately.</summary>
internal sealed class IngestionService(
    IngestionCoordinator coordinator,
    IngestionStatus status,
    IOptions<IngestionOptions> options,
    ILogger<IngestionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Ingestion is disabled by configuration");
            return;
        }

        try
        {
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
}
