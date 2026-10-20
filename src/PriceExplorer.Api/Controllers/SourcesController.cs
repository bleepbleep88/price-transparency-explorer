using MediatR;
using Microsoft.AspNetCore.Mvc;
using PriceExplorer.Api.Core.Models.Sources;
using PriceExplorer.Api.Core.Queries.Sources;

namespace PriceExplorer.Api.Controllers;

/// <summary>Where the data comes from and how it was cleaned.</summary>
[ApiController]
[Route("api/v1/sources")]
public sealed class SourcesController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Every snapshot and data file (rate files and out-of-network allowed-amount files), newest first: URL, the three
    /// dates, size, SHA-256, ETag, published → unique counts, rejects by reason with an example, and load times.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SourceSnapshot>>(StatusCodes.Status200OK, "application/json")]
    public Task<IReadOnlyList<SourceSnapshot>> List(CancellationToken cancellationToken) =>
        sender.Send(new ListSourcesQuery(), cancellationToken);
}
