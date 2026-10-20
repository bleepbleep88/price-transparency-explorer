using FluentValidation;
using MediatR;
using PriceExplorer.Api.Core.Behaviors;
using PriceExplorer.Api.Core.Models.Statistics;
using PriceExplorer.Api.Core.Queries.Rates;

namespace PriceExplorer.Api.Core.Queries.Statistics;

/// <summary>
/// A snapshot's saved statistics: each comparison group's numbers (group_stats) with its providers' values
/// (provider_values), the same numbers GET /api/v1/rates shows without provider filters. Internal: the history and
/// insights features read these instead of searching code by code. Rows from one call always come from one build.
/// </summary>
/// <param name="Snapshot">Snapshot id; defaults to the snapshot searches default to.</param>
/// <param name="Codes">Only these codes, in stored form (45378, 00812, 0490, J0585). Null for every code.</param>
/// <param name="ProviderKey">Only groups where this provider has a value, and only its value: a provider key from results
/// (an EIN, "u…" for a masked tax ID, or "type:value").</param>
/// <param name="IncludeProviders">False returns the groups' statistics only, with empty provider lists.</param>
internal sealed record GetGroupStatsQuery(
    int? Snapshot = null,
    IReadOnlyList<string>? Codes = null,
    string? ProviderKey = null,
    bool IncludeProviders = true) : IRequest<GroupStatsResult>, IRequiresData;

internal sealed class GetGroupStatsQueryValidator : AbstractValidator<GetGroupStatsQuery>
{
    public const int MaxCodes = 1_000;

    public GetGroupStatsQueryValidator()
    {
        RuleFor(query => query.Snapshot).GreaterThan(0).When(query => query.Snapshot is not null)
            .WithMessage("Snapshot id must be a positive number.");
        RuleFor(query => query.Codes)
            .Must(codes => codes!.Count is >= 1 and <= MaxCodes)
            .When(query => query.Codes is not null)
            .WithMessage($"List 1 to {MaxCodes:N0} codes, or none for every code.");
        RuleForEach(query => query.Codes)
            .Must(code => code is { Length: >= 1 and <= 8 } && code.All(char.IsAsciiLetterOrDigit))
            .WithMessage("A code is up to 8 letters and digits in its stored form, for example 45378, 00812 or 0490.");
        RuleFor(query => query.ProviderKey)
            .Must(InputRules.IsProviderKey)
            .When(query => !string.IsNullOrWhiteSpace(query.ProviderKey))
            .WithMessage("Provider must be an EIN or a provider key from search results.");
    }
}
