using FluentValidation;
using MediatR;
using PriceExplorer.Api.Core.Behaviors;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Models.Rates;

namespace PriceExplorer.Api.Core.Queries.Rates;

/// <summary>
/// Source details for one rate: file, snapshot, JSON path, raw values, and the provider groups it covers. With
/// <paramref name="Provider"/> (an EIN or provider key from a result row), that provider's groups are listed first.
/// </summary>
public sealed record GetRateSourceQuery(long RateId, string? Provider = null) : IRequest<RateSource>, IRequiresData;

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

/// <summary>Option lists for the search filters, and where the provider directory came from.</summary>
public sealed record GetReferenceListsQuery : IRequest<ReferenceLists>;

/// <summary>Typeahead: specialties (NUCC, from the national provider registry) among the NPIs in the files.</summary>
public sealed record SuggestSpecialtiesQuery(string? Q, int Limit = 10) : IRequest<IReadOnlyList<SpecialtySuggestion>>;

/// <summary>Input rules shared by the rate detail and typeahead queries.</summary>
internal static class InputRules
{
    /// <summary>An EIN (dash optional) or a provider key from search results ("u…" for masked tax IDs, "type:value").</summary>
    public static bool IsProviderKey(string? value) =>
        value is not null && !HasControlCharacters(value) && ProviderKeys.TryNormalizeEinFilter(value, out var key) && key is not null;

    /// <summary>Control characters (NUL included) can't be bound into a DuckDB string parameter, so they're rejected up front.</summary>
    public static bool HasControlCharacters(string value) => value.Any(char.IsControl);
}

public sealed class GetRateSourceQueryValidator : AbstractValidator<GetRateSourceQuery>
{
    public GetRateSourceQueryValidator()
    {
        RuleFor(query => query.Provider)
            .Must(InputRules.IsProviderKey)
            .When(query => !string.IsNullOrWhiteSpace(query.Provider))
            .WithMessage("Provider must be an EIN or a provider key from search results.");
    }
}

public sealed class ListRateNpisQueryValidator : AbstractValidator<ListRateNpisQuery>
{
    /// <summary>Covers every result row (the largest in the 2026-08-31 files has 60 rates) and keeps the URL under 8 KB.</summary>
    public const int MaxAlsoRates = 300;

    public ListRateNpisQueryValidator()
    {
        RuleFor(query => query.Provider)
            .Must(InputRules.IsProviderKey)
            .When(query => !string.IsNullOrWhiteSpace(query.Provider))
            .WithMessage("Provider must be an EIN or a provider key from search results.");
        RuleFor(query => query.Page).InclusiveBetween(1, SearchRatesQueryValidator.MaxPage)
            .WithMessage($"Page is between 1 and {SearchRatesQueryValidator.MaxPage:N0}.");
        RuleFor(query => query.PageSize).InclusiveBetween(1, 500).WithMessage("Page size is between 1 and 500.");
        RuleFor(query => query.AlsoRateIds)
            .Must(ids => ids!.Distinct().Count() <= MaxAlsoRates)
            .When(query => query.AlsoRateIds is not null)
            .OverridePropertyName("alsoRate")
            .WithMessage($"List at most {MaxAlsoRates} other rates.");
    }
}

public sealed class SuggestCodesQueryValidator : AbstractValidator<SuggestCodesQuery>
{
    public SuggestCodesQueryValidator()
    {
        RuleFor(query => query.Q)
            .Must(q => q is not null && q.Trim().Length is >= 1 and <= 60)
            .WithMessage("Type 1 to 60 characters: a code (45378, 0490, J0585) or words from its name (colonoscopy).");
        RuleFor(query => query.Q)
            .Must(q => !InputRules.HasControlCharacters(q!))
            .When(query => query.Q is not null)
            .WithMessage("Search text can't contain control characters.");
        RuleFor(query => query.Limit).InclusiveBetween(1, 50).WithMessage("Limit is between 1 and 50.");
        RuleFor(query => query.Snapshot).GreaterThan(0).When(query => query.Snapshot is not null).WithMessage("Snapshot id must be a positive number.");
    }
}

public sealed class SuggestSpecialtiesQueryValidator : AbstractValidator<SuggestSpecialtiesQuery>
{
    public SuggestSpecialtiesQueryValidator()
    {
        RuleFor(query => query.Q)
            .Must(q => q is not null && q.Trim().Length >= 2)
            .WithMessage("Type at least 2 characters of a specialty, for example 'gastro'.");
        RuleFor(query => query.Q)
            .Must(q => !InputRules.HasControlCharacters(q!))
            .When(query => query.Q is not null)
            .WithMessage("Search text can't contain control characters.");
        RuleFor(query => query.Q)
            .Must(q => q!.Trim().Length <= 100)
            .When(query => query.Q is not null)
            .WithMessage("Search text is at most 100 characters.");
        RuleFor(query => query.Limit).InclusiveBetween(1, 50).WithMessage("Limit is between 1 and 50.");
    }
}

public sealed class SuggestProvidersQueryValidator : AbstractValidator<SuggestProvidersQuery>
{
    public SuggestProvidersQueryValidator()
    {
        RuleFor(query => query.Q)
            .Must(q => q is not null && q.Trim().Length >= 2)
            .WithMessage("Type at least 2 characters of the provider's name or EIN.");
        RuleFor(query => query.Q)
            .Must(q => !InputRules.HasControlCharacters(q!))
            .When(query => query.Q is not null)
            .WithMessage("Search text can't contain control characters.");
        RuleFor(query => query.Q)
            .Must(q => q!.Trim().Length <= 100)
            .When(query => query.Q is not null)
            .WithMessage("Search text is at most 100 characters.");
        RuleFor(query => query.Limit).InclusiveBetween(1, 50).WithMessage("Limit is between 1 and 50.");
        RuleFor(query => query.Snapshot).GreaterThan(0).When(query => query.Snapshot is not null).WithMessage("Snapshot id must be a positive number.");
    }
}
