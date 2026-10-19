using MediatR;
using Microsoft.AspNetCore.Mvc;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Queries.Rates;

namespace PriceExplorer.Api.Controllers;

/// <summary>Typeahead and option lists for the search form.</summary>
[ApiController]
[Route("api/v1")]
public sealed class LookupController(ISender sender) : ControllerBase
{
    /// <summary>Codes with rates in the snapshot, matched by code (leading zeros optional) or published name.</summary>
    [HttpGet("codes")]
    [ProducesResponseType<IReadOnlyList<CodeSuggestion>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IReadOnlyList<CodeSuggestion>> Codes([FromQuery] string? q, [FromQuery] int? snapshot, [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default) =>
        sender.Send(new SuggestCodesQuery(q, snapshot, limit), cancellationToken);

    /// <summary>Providers matched by business name or exact EIN.</summary>
    [HttpGet("providers")]
    [ProducesResponseType<IReadOnlyList<ProviderSuggestion>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IReadOnlyList<ProviderSuggestion>> Providers([FromQuery] string? q, [FromQuery] int? snapshot, [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default) =>
        sender.Send(new SuggestProvidersQuery(q, snapshot, limit), cancellationToken);

    /// <summary>Care settings and sort options for the search filters.</summary>
    [HttpGet("reference")]
    [ProducesResponseType<ReferenceLists>(StatusCodes.Status200OK)]
    public Task<ReferenceLists> Reference(CancellationToken cancellationToken) =>
        sender.Send(new GetReferenceListsQuery(), cancellationToken);
}
