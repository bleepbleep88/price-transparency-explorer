using MediatR;
using PriceExplorer.Api.Core.Models.Sources;

namespace PriceExplorer.Api.Core.Queries.Sources;

/// <summary>
/// Every snapshot and data file, newest first: URLs, dates, checksums, counts, rejects and load times. Answers while
/// data is still loading (files not loaded yet show as pending), so it needs no loaded data.
/// </summary>
public sealed record ListSourcesQuery : IRequest<IReadOnlyList<SourceSnapshot>>;
