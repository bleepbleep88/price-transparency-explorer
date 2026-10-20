using MediatR;
using Microsoft.AspNetCore.Mvc;
using PriceExplorer.Api.Core.Models.History;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Queries.History;
using PriceExplorer.Api.Core.Queries.Rates;

namespace PriceExplorer.Api.Controllers;

/// <summary>Negotiated rate search and per-rate source details.</summary>
[ApiController]
[Route("api/v1/rates")]
public sealed class RatesController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Rates for a billing code (required), optionally narrowed to a provider by NPI and/or EIN (they must match each other)
    /// or by name, or to providers with a specialty or practicing in a New York county or NYC borough. Results come back as
    /// fair comparison groups (one value per provider) plus paged provider rows.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<RateSearchResult>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public Task<RateSearchResult> Search([FromQuery] SearchRatesQuery query, CancellationToken cancellationToken) =>
        sender.Send(query, cancellationToken);

    /// <summary>
    /// How each comparison group a search shows moved across the loaded snapshots: provider count, median and quartiles
    /// in every snapshot, plus the searched provider's rate. Takes the search's filters; the search runs in each
    /// snapshot, so each point equals what that snapshot's search shows.
    /// </summary>
    [HttpGet("trend")]
    [ProducesResponseType<RateTrendResult>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public Task<RateTrendResult> Trend([FromQuery] GetRateTrendQuery query, CancellationToken cancellationToken) =>
        sender.Send(query, cancellationToken);

    /// <summary>
    /// The level ladder for an ER or office visit code: for each plan, fee type, setting, modifier set and rate type, every
    /// level's provider count and statistics, the same numbers each level's search shows.
    /// </summary>
    /// <param name="code">Required. An ER or office visit code: 99281 to 99285, 99202 to 99205, or 99211 to 99215.</param>
    /// <param name="snapshot">Optional snapshot id; defaults to the newest loaded snapshot.</param>
    [HttpGet("levels")]
    [ProducesResponseType<LevelLadder>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public Task<LevelLadder> Levels([FromQuery] string? code, [FromQuery] int? snapshot, CancellationToken cancellationToken) =>
        sender.Send(new GetLevelLadderQuery(code, snapshot), cancellationToken);

    /// <summary>
    /// Where a rate came from: file, snapshot, JSON path, values as published, and the provider groups it covers. Pass
    /// <c>provider</c> (the row's provider key) to list that provider's groups first.
    /// </summary>
    [HttpGet("{rateId:long}/source")]
    [ProducesResponseType<RateSource>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public Task<RateSource> Source(long rateId, [FromQuery] string? provider, CancellationToken cancellationToken) =>
        sender.Send(new GetRateSourceQuery(rateId, provider), cancellationToken);

    /// <summary>
    /// NPIs listed for a rate, optionally only those under one provider (EIN or provider key from results). Repeat
    /// <c>alsoRate</c> (up to 300) to include the other rates behind a result row; each NPI then shows the rate it gets.
    /// </summary>
    [HttpGet("{rateId:long}/npis")]
    [ProducesResponseType<RateNpisPage>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public Task<RateNpisPage> Npis(long rateId, [FromQuery] string? provider, [FromQuery] long[]? alsoRate, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) =>
        sender.Send(new ListRateNpisQuery(rateId, provider, page, pageSize, alsoRate), cancellationToken);
}
