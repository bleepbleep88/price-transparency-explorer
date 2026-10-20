using MediatR;
using Microsoft.AspNetCore.Mvc;
using PriceExplorer.Api.Core.Models.OutOfNetwork;
using PriceExplorer.Api.Core.Queries.OutOfNetwork;

namespace PriceExplorer.Api.Controllers;

/// <summary>Out-of-network allowed amounts: what the plan allowed when it paid providers outside its network.</summary>
[ApiController]
[Route("api/v1/out-of-network")]
public sealed class OutOfNetworkController(ISender sender) : ControllerBase
{
    /// <summary>
    /// A code's out-of-network payments per product and fee type: how many, to how many providers, and the allowed
    /// amounts and billed charges (listed under 5, else min, median and max). Plans list a provider and service only
    /// with 20 or more claims in a 90-day window, so most codes have none.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<OutOfNetworkResult>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public Task<OutOfNetworkResult> Get([FromQuery] GetOutOfNetworkQuery query, CancellationToken cancellationToken) =>
        sender.Send(query, cancellationToken);
}
