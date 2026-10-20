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
    /// <param name="q">Required. 1 to 60 characters: a code (45378, 0490, J0585) or words from its name.</param>
    /// <param name="snapshot">Optional snapshot id; defaults to the newest loaded snapshot.</param>
    /// <param name="limit">How many suggestions, 1 to 50.</param>
    [HttpGet("codes")]
    [ProducesResponseType<IReadOnlyList<CodeSuggestion>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public Task<IReadOnlyList<CodeSuggestion>> Codes([FromQuery] string? q, [FromQuery] int? snapshot, [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default) =>
        sender.Send(new SuggestCodesQuery(q, snapshot, limit), cancellationToken);

    /// <summary>Providers matched by business name or exact EIN.</summary>
    /// <param name="q">Required. At least 2 characters of a business name, or an EIN.</param>
    /// <param name="snapshot">Optional snapshot id; defaults to the newest loaded snapshot.</param>
    /// <param name="limit">How many suggestions, 1 to 50.</param>
    [HttpGet("providers")]
    [ProducesResponseType<IReadOnlyList<ProviderSuggestion>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public Task<IReadOnlyList<ProviderSuggestion>> Providers([FromQuery] string? q, [FromQuery] int? snapshot, [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default) =>
        sender.Send(new SuggestProvidersQuery(q, snapshot, limit), cancellationToken);

    /// <summary>Care settings and sort options for the search filters.</summary>
    [HttpGet("reference")]
    [ProducesResponseType<ReferenceLists>(StatusCodes.Status200OK, "application/json")]
    public Task<ReferenceLists> Reference(CancellationToken cancellationToken) =>
        sender.Send(new GetReferenceListsQuery(), cancellationToken);
}
