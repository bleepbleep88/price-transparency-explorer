using MediatR;
using PriceExplorer.Api.Core.Ingestion;
using PriceExplorer.Api.Core.Queries.Status;

namespace PriceExplorer.Api.Core.Handlers.Status;

public sealed class GetIngestionStatusHandler(IngestionStatus status) : IRequestHandler<GetIngestionStatusQuery, IngestionStatusSnapshot>
{
    public Task<IngestionStatusSnapshot> Handle(GetIngestionStatusQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(status.Current);
}

public sealed class WatchIngestionStatusHandler(IngestionStatus status) : IStreamRequestHandler<WatchIngestionStatusQuery, IngestionStatusSnapshot>
{
    public IAsyncEnumerable<IngestionStatusSnapshot> Handle(WatchIngestionStatusQuery request, CancellationToken cancellationToken) =>
        status.WatchAsync(cancellationToken);
}
