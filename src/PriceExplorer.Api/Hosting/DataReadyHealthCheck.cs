using Microsoft.Extensions.Diagnostics.HealthChecks;
using PriceExplorer.Api.Core.Ingestion;

namespace PriceExplorer.Api.Hosting;

/// <summary>Ready once at least one snapshot can be queried. Tagged "ready" so /health/ready returns 503 until then.</summary>
internal sealed class DataReadyHealthCheck(IngestionStatus status) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var current = status.Current;
        return Task.FromResult(current.DataAvailable
            ? HealthCheckResult.Healthy(current.Message)
            : HealthCheckResult.Unhealthy(current.Message));
    }
}
