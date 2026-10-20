using MediatR;
using Microsoft.AspNetCore.Mvc;
using PriceExplorer.Api.Core.Models.Insights;
using PriceExplorer.Api.Core.Queries.Insights;

namespace PriceExplorer.Api.Controllers;

/// <summary>
/// Views across many codes, read from the saved statistics (the numbers search shows, saved for every group once a
/// snapshot loads). Each response says which snapshot it describes and whether its statistics are current.
/// </summary>
[ApiController]
[Route("api/v1/insights")]
public sealed class InsightsController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Comparison groups with 10 or more providers, ranked by the gap to the lower quartile (median − 25th percentile)
    /// or by spread (90th ÷ 10th percentile). The files have no claim counts, so a gap is per case or per unit.
    /// </summary>
    [HttpGet("gaps")]
    [ProducesResponseType<GapLeaderboard>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public Task<GapLeaderboard> Gaps([FromQuery] GetGapLeaderboardQuery query, CancellationToken cancellationToken) =>
        sender.Send(query, cancellationToken);

    /// <summary>
    /// One provider (tax ID) across every comparison group it has a rate in: its rate, percentile and × median in each
    /// (as a search with its EIN shows them), its highest and lowest against the median, and a price index. The provider
    /// is a query parameter, like the EIN filter of search, so a full masked tax ID never lands in a request path or log.
    /// </summary>
    [HttpGet("scorecard")]
    [ProducesResponseType<ProviderScorecard>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public Task<ProviderScorecard> Scorecard([FromQuery] GetProviderScorecardQuery query, CancellationToken cancellationToken) =>
        sender.Send(query, cancellationToken);

    /// <summary>
    /// The same provider in the same comparison group under two products, as the ratio product ÷ versus: how the ratios
    /// spread, how many are identical, by category, and the biggest differences.
    /// </summary>
    [HttpGet("compare")]
    [ProducesResponseType<ProductComparison>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public Task<ProductComparison> Compare([FromQuery] GetProductComparisonQuery query, CancellationToken cancellationToken) =>
        sender.Send(query, cancellationToken);
}
