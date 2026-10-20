using FluentValidation;
using MediatR;
using PriceExplorer.Api.Core.Behaviors;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Models.Insights;
using PriceExplorer.Api.Core.Queries.Rates;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Api.Core.Queries.Insights;

/// <summary>Comparison groups with 10 or more providers, ranked by the gap to the lower quartile or by spread.</summary>
/// <param name="Product">Optional product key, e.g. "essential-plan".</param>
/// <param name="Category">Optional category of service key, e.g. "surgery" (the response lists the keys).</param>
/// <param name="Setting">Optional care setting key, e.g. "asc", "hospital-outpatient", "office".</param>
/// <param name="FeeType">Optional: "facility" or "professional".</param>
/// <param name="Code">Optional billing code as typed ("45378", "0490", "812").</param>
/// <param name="IncludeFacilityCharges">Also rank charges filed under a revenue code with no procedure named (RC 0360
/// "Operating room"). Off by default: hospitals bundle different services under them, so they don't compare like for like.</param>
/// <param name="Sort">gap (largest gap to the lower quartile first; the default) | spread (largest 90th ÷ 10th percentile
/// first) | range (largest 90th − 10th percentile, in dollars, first). Groups with no gap are never ranked.</param>
/// <param name="Snapshot">Optional snapshot id; defaults to the newest loaded snapshot.</param>
public sealed record GetGapLeaderboardQuery(
    string? Product = null,
    string? Category = null,
    string? Setting = null,
    string? FeeType = null,
    string? Code = null,
    bool IncludeFacilityCharges = false,
    string? Sort = null,
    int? Snapshot = null,
    int Page = 1,
    int PageSize = 25) : IRequest<GapLeaderboard>, IRequiresData;

/// <summary>One provider (tax ID) across every comparison group it has a rate in, with its price index.</summary>
/// <param name="Provider">Required. An EIN (11-3471596) or a provider key from results.</param>
/// <param name="Snapshot">Optional snapshot id; defaults to the newest loaded snapshot.</param>
/// <param name="IncludeFacilityCharges">Also count charges filed under a revenue code with no procedure named in the
/// price index and the highest and lowest lists. Off by default: they don't compare like for like. They're always in
/// the list of groups.</param>
public sealed record GetProviderScorecardQuery(string? Provider = null, int? Snapshot = null, bool IncludeFacilityCharges = false)
    : IRequest<ProviderScorecard>, IRequiresData;

/// <summary>
/// The same provider in the same comparison group under two products: product ÷ versus. Without products, the
/// snapshot's two products are compared, the second by name against the first (Essential Plan ÷ Ambetter here).
/// </summary>
/// <param name="Product">Optional product key whose rates are compared (the numerator).</param>
/// <param name="Versus">Optional product key they are compared with (the denominator).</param>
/// <param name="Code">Optional billing code as typed ("45378", "0490", "812").</param>
/// <param name="Category">Optional category of service key, e.g. "surgery" (the response lists the keys).</param>
/// <param name="Setting">Optional care setting key, e.g. "asc".</param>
/// <param name="IncludeFacilityCharges">Also compare charges filed under a revenue code with no procedure named. Off by
/// default: they don't compare like for like.</param>
/// <param name="Sort">difference (biggest difference either way first; the default) | ratio (lowest first) | -ratio (highest first).</param>
/// <param name="Snapshot">Optional snapshot id; defaults to the newest loaded snapshot.</param>
public sealed record GetProductComparisonQuery(
    string? Product = null,
    string? Versus = null,
    string? Code = null,
    string? Category = null,
    string? Setting = null,
    bool IncludeFacilityCharges = false,
    string? Sort = null,
    int? Snapshot = null,
    int Page = 1,
    int PageSize = 25) : IRequest<ProductComparison>, IRequiresData;

/// <summary>Input rules the insights share with search: same shapes, same messages.</summary>
internal static partial class InsightInput
{
    public const string ProductMessage = "A product is a product key from the snapshot, for example essential-plan.";
    public const string CategoryMessage = "A category is a category key from the results, for example surgery.";
    public const string CodeMessage = "A billing code is up to 8 letters and digits, for example 45378, J0585 or 0490.";
    public const string FeeTypeMessage = "Fee type is either 'facility' or 'professional'.";

    public static bool IsProductKey(string value) => !InputRules.HasControlCharacters(value) && SearchRatesQueryValidator.IsProductKey(value);

    public static bool IsCategoryKey(string value) => CategoryKey().IsMatch(value);

    /// <summary>A code as typed, with an optional type word ("CPT 99213", "rc 0490"), the way search reads it.</summary>
    public static bool IsCode(string value) =>
        CodeInput.Parse(value, null).Code is { Length: > 0 and <= 8 } clean && clean.All(char.IsAsciiLetterOrDigit);

    /// <summary>The stored codes a typed code can mean ("812" → 812, 00812, 0812), and the code type a type word named.</summary>
    public static (IReadOnlyList<string> Codes, string? CodeType) Codes(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
        {
            return ([], null);
        }

        var (code, codeType) = CodeInput.Parse(typed, null);
        return (CodeInput.Candidates(code, codeType), codeType);
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")]
    private static partial System.Text.RegularExpressions.Regex CategoryKey();
}

public sealed class GetGapLeaderboardQueryValidator : AbstractValidator<GetGapLeaderboardQuery>
{
    public static readonly string[] Sorts = ["gap", "spread", "range"];

    public GetGapLeaderboardQueryValidator(ReferenceCatalog catalog)
    {
        RuleFor(query => query.Product).Must(product => InsightInput.IsProductKey(product!))
            .When(query => !string.IsNullOrWhiteSpace(query.Product)).WithMessage(InsightInput.ProductMessage);
        RuleFor(query => query.Category).Must(category => InsightInput.IsCategoryKey(category!))
            .When(query => !string.IsNullOrWhiteSpace(query.Category)).WithMessage(InsightInput.CategoryMessage);
        RuleFor(query => query.Setting).Must(setting => catalog.Setting(setting!) is not null)
            .When(query => !string.IsNullOrWhiteSpace(query.Setting))
            .WithMessage($"Setting must be one of: {string.Join(", ", catalog.Settings.Select(s => s.Key))}.");
        RuleFor(query => query.FeeType).Must(feeType => feeType is "facility" or "professional")
            .When(query => !string.IsNullOrWhiteSpace(query.FeeType)).WithMessage(InsightInput.FeeTypeMessage);
        RuleFor(query => query.Code).Must(code => InsightInput.IsCode(code!))
            .When(query => !string.IsNullOrWhiteSpace(query.Code)).WithMessage(InsightInput.CodeMessage);
        RuleFor(query => query.Sort).Must(sort => Sorts.Contains(sort))
            .When(query => query.Sort is not null).WithMessage($"Sort must be one of: {string.Join(", ", Sorts)}.");
        RuleFor(query => query.Snapshot).GreaterThan(0).When(query => query.Snapshot is not null)
            .WithMessage("Snapshot id must be a positive number.");
        RuleFor(query => query.Page).InclusiveBetween(1, SearchRatesQueryValidator.MaxPage)
            .WithMessage($"Page is between 1 and {SearchRatesQueryValidator.MaxPage:N0}.");
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100).WithMessage("Page size is between 1 and 100.");
    }
}

public sealed class GetProviderScorecardQueryValidator : AbstractValidator<GetProviderScorecardQuery>
{
    public GetProviderScorecardQueryValidator()
    {
        RuleFor(query => query.Provider).Must(InputRules.IsProviderKey)
            .WithMessage("Enter a 9-digit EIN, for example 11-3471596, or a provider key from search results.");
        RuleFor(query => query.Snapshot).GreaterThan(0).When(query => query.Snapshot is not null)
            .WithMessage("Snapshot id must be a positive number.");
    }
}

public sealed class GetProductComparisonQueryValidator : AbstractValidator<GetProductComparisonQuery>
{
    public static readonly string[] Sorts = ["difference", "ratio", "-ratio"];

    public GetProductComparisonQueryValidator(ReferenceCatalog catalog)
    {
        RuleFor(query => query.Product).Must(product => InsightInput.IsProductKey(product!))
            .When(query => !string.IsNullOrWhiteSpace(query.Product)).WithMessage(InsightInput.ProductMessage);
        RuleFor(query => query.Versus).Must(product => InsightInput.IsProductKey(product!))
            .When(query => !string.IsNullOrWhiteSpace(query.Versus)).WithMessage(InsightInput.ProductMessage);
        RuleFor(query => query.Versus)
            .Must((query, versus) => !string.Equals(query.Product!.Trim(), versus!.Trim(), StringComparison.Ordinal))
            .When(query => !string.IsNullOrWhiteSpace(query.Product) && !string.IsNullOrWhiteSpace(query.Versus))
            .WithMessage("Pick two different products to compare.");
        RuleFor(query => query.Code).Must(code => InsightInput.IsCode(code!))
            .When(query => !string.IsNullOrWhiteSpace(query.Code)).WithMessage(InsightInput.CodeMessage);
        RuleFor(query => query.Category).Must(category => InsightInput.IsCategoryKey(category!))
            .When(query => !string.IsNullOrWhiteSpace(query.Category)).WithMessage(InsightInput.CategoryMessage);
        RuleFor(query => query.Setting).Must(setting => catalog.Setting(setting!) is not null)
            .When(query => !string.IsNullOrWhiteSpace(query.Setting))
            .WithMessage($"Setting must be one of: {string.Join(", ", catalog.Settings.Select(s => s.Key))}.");
        RuleFor(query => query.Sort).Must(sort => Sorts.Contains(sort))
            .When(query => query.Sort is not null).WithMessage($"Sort must be one of: {string.Join(", ", Sorts)}.");
        RuleFor(query => query.Snapshot).GreaterThan(0).When(query => query.Snapshot is not null)
            .WithMessage("Snapshot id must be a positive number.");
        RuleFor(query => query.Page).InclusiveBetween(1, SearchRatesQueryValidator.MaxPage)
            .WithMessage($"Page is between 1 and {SearchRatesQueryValidator.MaxPage:N0}.");
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100).WithMessage("Page size is between 1 and 100.");
    }
}
