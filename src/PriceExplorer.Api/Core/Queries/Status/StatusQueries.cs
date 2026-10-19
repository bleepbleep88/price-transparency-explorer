using MediatR;
using PriceExplorer.Api.Core.Ingestion;

namespace PriceExplorer.Api.Core.Queries.Status;

/// <summary>Current loading status.</summary>
public sealed record GetIngestionStatusQuery : IRequest<IngestionStatusSnapshot>;

/// <summary>Loading status as a live stream: the current value, then every change.</summary>
public sealed record WatchIngestionStatusQuery : IStreamRequest<IngestionStatusSnapshot>;
