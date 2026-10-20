using MediatR;
using Microsoft.AspNetCore.Mvc;
using PriceExplorer.Api.Core.Models.History;
using PriceExplorer.Api.Core.Queries.History;

namespace PriceExplorer.Api.Controllers;

/// <summary>How negotiated rates changed between snapshots.</summary>
[ApiController]
[Route("api/v1/changes")]
public sealed class ChangesController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Rates that changed between two snapshots: the same provider (tax ID) in the same comparison group with a different
    /// rate, plus providers only in the newer file (new) or only in the older one (dropped). Products whose file looks
    /// partial in either snapshot are left out, and the response says why. A change means the file says something
    /// different; it isn't a claim. Until both snapshots' saved statistics are built and current, the response has
    /// current: false, a message, and no counts or rows. Facility charges with no procedure named are left out
    /// unless includeFacilityCharges is set; counts.facilityCharges says how many there are.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<ChangesResult>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public Task<ChangesResult> List([FromQuery] GetChangesQuery query, CancellationToken cancellationToken) =>
        sender.Send(query, cancellationToken);
}
