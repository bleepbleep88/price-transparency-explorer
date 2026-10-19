using System.Runtime.CompilerServices;
using MediatR;
using PriceExplorer.Api.Core.Ingestion;
using PriceExplorer.Api.Core.Queries.Status;

namespace PriceExplorer.Api.Core.Handlers.Status;

public sealed class GetIngestionStatusHandler(IngestionStatus status) : IRequestHandler<GetIngestionStatusQuery, IngestionStatusSnapshot>
{
    public Task<IngestionStatusSnapshot> Handle(GetIngestionStatusQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(status.Current);
}

public sealed class WatchIngestionStatusHandler(IngestionStatus status, IHostApplicationLifetime lifetime)
    : IStreamRequestHandler<WatchIngestionStatusQuery, IngestionStatusSnapshot>
{
    /// <summary>Ends when the client disconnects or the app starts shutting down, so an open browser tab never delays a stop.</summary>
    public async IAsyncEnumerable<IngestionStatusSnapshot> Handle(
        WatchIngestionStatusQuery request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.ApplicationStopping);
        await foreach (var snapshot in status.WatchAsync(stopping.Token))
        {
            yield return snapshot;
        }
    }
}
