using MediatR;
using Microsoft.AspNetCore.Mvc;
using PriceExplorer.Api.Core.Ingestion;
using PriceExplorer.Api.Core.Queries.Status;

namespace PriceExplorer.Api.Controllers;

/// <summary>Data loading status.</summary>
[ApiController]
[Route("api/v1/status")]
public sealed class StatusController(ISender sender) : ControllerBase
{
    /// <summary>What the loader is doing now and whether data can be queried.</summary>
    [HttpGet]
    public Task<IngestionStatusSnapshot> Get(CancellationToken cancellationToken) =>
        sender.Send(new GetIngestionStatusQuery(), cancellationToken);

    /// <summary>Live loading status as server-sent events (event name "status"): the current value, then every change.</summary>
    [HttpGet("stream")]
    [Produces("text/event-stream")]
    public IResult Stream(CancellationToken cancellationToken) =>
        TypedResults.ServerSentEvents(sender.CreateStream(new WatchIngestionStatusQuery(), cancellationToken), eventType: "status");
}
