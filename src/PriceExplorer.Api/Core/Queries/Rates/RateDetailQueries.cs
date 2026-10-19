using FluentValidation;
using MediatR;
using PriceExplorer.Api.Core.Behaviors;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Models.Rates;

namespace PriceExplorer.Api.Core.Queries.Rates;

/// <summary>Source details for one rate: file, snapshot, JSON path, raw values, and the provider groups it covers.</summary>
public sealed record GetRateSourceQuery(long RateId) : IRequest<RateSource>, IRequiresData;

/// <summary>
/// NPIs the file lists for a rate, optionally only those under one provider (tax ID). <paramref name="AlsoRateIds"/> adds
/// the other rates behind the same result row, so a provider with several rates shows every NPI with the rate it gets.
/// </summary>
public sealed record ListRateNpisQuery(long RateId, string? Provider = null, int Page = 1, int PageSize = 50, IReadOnlyList<long>? AlsoRateIds = null)
    : IRequest<RateNpisPage>, IRequiresData;

/// <summary>Typeahead: codes in the snapshot whose code or published name matches what's typed.</summary>
public sealed record SuggestCodesQuery(string? Q, int? Snapshot = null, int Limit = 10)
    : IRequest<IReadOnlyList<CodeSuggestion>>, IRequiresData;

/// <summary>Typeahead: providers whose business name (or EIN) matches what's typed.</summary>
public sealed record SuggestProvidersQuery(string? Q, int? Snapshot = null, int Limit = 10)
    : IRequest<IReadOnlyList<ProviderSuggestion>>, IRequiresData;

/// <summary>Option lists for the search filters.</summary>
public sealed record GetReferenceListsQuery : IRequest<ReferenceLists>;

public sealed class ListRateNpisQueryValidator : AbstractValidator<ListRateNpisQuery>
{
    public ListRateNpisQueryValidator()
    {
        RuleFor(query => query.Provider)
            .Must(provider => provider!.Contains(':', StringComparison.Ordinal) || ProviderKeys.TryNormalizeEinFilter(provider, out _))
            .When(query => !string.IsNullOrWhiteSpace(query.Provider))
            .WithMessage("Provider must be an EIN or a provider key from search results.");
        RuleFor(query => query.Page).InclusiveBetween(1, SearchRatesQueryValidator.MaxPage)
            .WithMessage($"Page is between 1 and {SearchRatesQueryValidator.MaxPage:N0}.");
        RuleFor(query => query.PageSize).InclusiveBetween(1, 500).WithMessage("Page size is between 1 and 500.");
        RuleFor(query => query.AlsoRateIds)
            .Must(ids => ids!.Count <= 50)
            .When(query => query.AlsoRateIds is not null)
            .WithMessage("List at most 50 other rates.");
    }
}

public sealed class SuggestCodesQueryValidator : AbstractValidator<SuggestCodesQuery>
{
    public SuggestCodesQueryValidator()
    {
        RuleFor(query => query.Q)
            .Must(q => q is not null && q.Trim().Length is >= 1 and <= 60)
            .WithMessage("Type 1 to 60 characters: a code (45378, 0490, J0585) or words from its name (colonoscopy).");
        RuleFor(query => query.Limit).InclusiveBetween(1, 50).WithMessage("Limit is between 1 and 50.");
        RuleFor(query => query.Snapshot).GreaterThan(0).When(query => query.Snapshot is not null).WithMessage("Snapshot id must be a positive number.");
    }
}

public sealed class SuggestProvidersQueryValidator : AbstractValidator<SuggestProvidersQuery>
{
    public SuggestProvidersQueryValidator()
    {
        RuleFor(query => query.Q)
            .Must(q => q is not null && q.Trim().Length is >= 2 and <= 100)
            .WithMessage("Type at least 2 characters of the provider's name or EIN.");
        RuleFor(query => query.Limit).InclusiveBetween(1, 50).WithMessage("Limit is between 1 and 50.");
        RuleFor(query => query.Snapshot).GreaterThan(0).When(query => query.Snapshot is not null).WithMessage("Snapshot id must be a positive number.");
    }
}
