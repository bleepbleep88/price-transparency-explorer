using MediatR;
using Microsoft.AspNetCore.Mvc;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Queries.Rates;

namespace PriceExplorer.Api.Controllers;

/// <summary>Negotiated rate search and per-rate source details.</summary>
[ApiController]
[Route("api/v1/rates")]
public sealed class RatesController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Rates for a billing code (required), optionally narrowed to a provider by NPI and/or EIN (they must match each other)
    /// or by name. Results come back as fair comparison groups (one value per provider) plus paged provider rows.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<RateSearchResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<RateSearchResult> Search([FromQuery] SearchRatesQuery query, CancellationToken cancellationToken) =>
        sender.Send(query, cancellationToken);

    /// <summary>
    /// Where a rate came from: file, snapshot, JSON path, values as published, and the provider groups it covers. Pass
    /// <c>provider</c> (the row's provider key) to list that provider's groups first.
    /// </summary>
    [HttpGet("{rateId:long}/source")]
    [ProducesResponseType<RateSource>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<RateSource> Source(long rateId, [FromQuery] string? provider, CancellationToken cancellationToken) =>
        sender.Send(new GetRateSourceQuery(rateId, provider), cancellationToken);

    /// <summary>
    /// NPIs listed for a rate, optionally only those under one provider (EIN or provider key from results). Repeat
    /// <c>alsoRate</c> (up to 300) to include the other rates behind a result row; each NPI then shows the rate it gets.
    /// </summary>
    [HttpGet("{rateId:long}/npis")]
    [ProducesResponseType<RateNpisPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<RateNpisPage> Npis(long rateId, [FromQuery] string? provider, [FromQuery] long[]? alsoRate, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) =>
        sender.Send(new ListRateNpisQuery(rateId, provider, page, pageSize, alsoRate), cancellationToken);
}
