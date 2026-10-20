using MediatR;
using Microsoft.AspNetCore.Mvc;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Queries.Categories;
using PriceExplorer.Api.Core.Queries.Rates;

namespace PriceExplorer.Api.Controllers;

/// <summary>Typeahead and option lists for the search form.</summary>
[ApiController]
[Route("api/v1")]
public sealed class LookupController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Codes with rates in the snapshot, matched by code (leading zeros optional) or name: the plan's own, else CMS's HCPCS
    /// Level II name. Optionally only codes in one category.
    /// </summary>
    /// <param name="q">1 to 60 characters: a code (45378, 0490, J0585) or words from its name. Required unless a category is
    /// given; then, without it, the category's codes come most rates first.</param>
    /// <param name="snapshot">Optional snapshot id; defaults to the newest loaded snapshot.</param>
    /// <param name="limit">How many suggestions, 1 to 50.</param>
    /// <param name="category">Optional category key from /api/v1/categories: an RBCS category (P), subcategory (PG) or family
    /// (PG004), or a code-range category (facility).</param>
    [HttpGet("codes")]
    [ProducesResponseType<IReadOnlyList<CodeSuggestion>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public Task<IReadOnlyList<CodeSuggestion>> Codes([FromQuery] string? q, [FromQuery] int? snapshot, [FromQuery] int limit = 10,
        [FromQuery] string? category = null, CancellationToken cancellationToken = default) =>
        sender.Send(new SuggestCodesQuery(q, snapshot, limit, string.IsNullOrWhiteSpace(category) ? null : category.Trim()), cancellationToken);

    /// <summary>
    /// The categories of the codes with rates in the snapshot, with code and rate counts: CMS's RBCS categories (with
    /// their subcategories and families), then code-range categories for codes RBCS doesn't list, such as revenue codes.
    /// </summary>
    /// <param name="snapshot">Optional snapshot id; defaults to the newest loaded snapshot.</param>
    [HttpGet("categories")]
    [ProducesResponseType<CategoryTree>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public Task<CategoryTree> Categories([FromQuery] int? snapshot, CancellationToken cancellationToken) =>
        sender.Send(new GetCategoriesQuery(snapshot), cancellationToken);

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

    /// <summary>Specialties (NUCC, from the national provider registry) that the specialty filter matches for the text.</summary>
    /// <param name="q">Required. 2 to 100 characters: words that start words of a specialty name ("gastro", "cardiologist", "ENT"), or a taxonomy code.</param>
    /// <param name="limit">How many suggestions, 1 to 50.</param>
    [HttpGet("specialties")]
    [ProducesResponseType<IReadOnlyList<SpecialtySuggestion>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public Task<IReadOnlyList<SpecialtySuggestion>> Specialties([FromQuery] string? q, [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default) =>
        sender.Send(new SuggestSpecialtiesQuery(q, limit), cancellationToken);

    /// <summary>
    /// Care settings and sort options for the search filters, New York counties and NYC boroughs for the location
    /// filter, and the release of the national provider registry behind specialties and locations.
    /// </summary>
    [HttpGet("reference")]
    [ProducesResponseType<ReferenceLists>(StatusCodes.Status200OK, "application/json")]
    public Task<ReferenceLists> Reference(CancellationToken cancellationToken) =>
        sender.Send(new GetReferenceListsQuery(), cancellationToken);
}
