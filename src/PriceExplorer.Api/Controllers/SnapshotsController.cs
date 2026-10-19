using MediatR;
using Microsoft.AspNetCore.Mvc;
using PriceExplorer.Api.Core.Models.Snapshots;
using PriceExplorer.Api.Core.Queries.Snapshots;

namespace PriceExplorer.Api.Controllers;

/// <summary>Loaded snapshots (index files), their rate files, and the plans they cover.</summary>
[ApiController]
[Route("api/v1/snapshots")]
public sealed class SnapshotsController(ISender sender) : ControllerBase
{
    /// <summary>Every snapshot, newest first, with each product's rate file, source details, and load results.</summary>
    [HttpGet]
    public Task<IReadOnlyList<SnapshotSummary>> List(CancellationToken cancellationToken) =>
        sender.Send(new ListSnapshotsQuery(), cancellationToken);

    /// <summary>Plans covered by a snapshot, grouped by product. Plans in one product share the same rates.</summary>
    [HttpGet("{snapshotId:int}/plans")]
    [ProducesResponseType<SnapshotPlans>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SnapshotPlans> Plans(int snapshotId, CancellationToken cancellationToken) =>
        sender.Send(new ListPlansQuery(snapshotId), cancellationToken);
}
