using FluentValidation;
using MediatR;
using PriceExplorer.Api.Core.Behaviors;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Api.Core.Queries.Rates;

/// <summary>Negotiated rates for one billing code, optionally narrowed to a provider by NPI, EIN, or name.</summary>
/// <param name="Code">Required. CPT/HCPCS/revenue code as typed ("45378", "j0585", "490").</param>
/// <param name="CodeType">Optional: CPT, HCPCS, RC... Needed only when a short number is ambiguous.</param>
/// <param name="Npi">Optional 10-digit NPI. Combined with an EIN, it must bill under that EIN.</param>
/// <param name="Ein">Optional 9-digit EIN (dash optional), or a provider key from a previous result.</param>
/// <param name="Product">Optional product key, e.g. "essential-plan".</param>
/// <param name="Setting">Optional care setting key, e.g. "asc", "hospital-outpatient", "office".</param>
/// <param name="FeeType">Optional: "facility" or "professional".</param>
/// <param name="Provider">Optional text matched against provider business names.</param>
/// <param name="Group">Optional comparison group key: limits the rows (not the group cards) to that group.</param>
/// <param name="Snapshot">Optional snapshot id; defaults to the newest loaded snapshot.</param>
/// <param name="Sort">rate | -rate | provider | -provider | vsMedian | -vsMedian.</param>
public sealed record SearchRatesQuery(
    string? Code,
    string? CodeType = null,
    string? Npi = null,
    string? Ein = null,
    string? Product = null,
    string? Setting = null,
    string? FeeType = null,
    string? Provider = null,
    string? Group = null,
    int? Snapshot = null,
    string? Sort = null,
    int Page = 1,
    int PageSize = 25) : IRequest<RateSearchResult>, IRequiresData;

public sealed class SearchRatesQueryValidator : AbstractValidator<SearchRatesQuery>
{
    public static readonly string[] Sorts = ["rate", "-rate", "provider", "-provider", "vsMedian", "-vsMedian"];

    // Code types allowed by the CMS in-network schema.
    public static readonly string[] CodeTypes =
        ["CPT", "HCPCS", "RC", "NDC", "ICD", "MS-DRG", "R-DRG", "S-DRG", "APS-DRG", "AP-DRG", "APR-DRG", "APC", "LOCAL", "EAPG", "HIPPS", "CDT", "CSTM-ALL"];

    public SearchRatesQueryValidator(ReferenceCatalog catalog)
    {
        // Two separate rules: a trailing When() applies to every check in its chain, which would disable "required".
        RuleFor(query => query.Code)
            .Must(code => CodeInput.Clean(code).Length > 0)
            .WithMessage("Enter a billing code, for example 45378 (colonoscopy) or 0490 (ambulatory surgery).");

        RuleFor(query => query.Code)
            .Must(code => CodeInput.Clean(code) is { Length: <= 8 } clean && clean.All(char.IsAsciiLetterOrDigit))
            .When(query => CodeInput.Clean(query.Code).Length > 0)
            .WithMessage("A billing code is up to 8 letters and digits, for example 45378, J0585 or 0490.");

        RuleFor(query => query.CodeType)
            .Must(type => CodeTypes.Contains(type!.Trim().ToUpperInvariant()))
            .When(query => !string.IsNullOrWhiteSpace(query.CodeType))
            .WithMessage($"Code type must be one of: {string.Join(", ", CodeTypes)}.");

        RuleFor(query => query.Npi)
            .Must(npi => npi!.All(c => char.IsAsciiDigit(c) || c is ' ' or '-') && Domain.Npi.Parse(npi) is not null)
            .When(query => !string.IsNullOrWhiteSpace(query.Npi))
            .WithMessage("An NPI is 10 digits, for example 1396747861.");

        RuleFor(query => query.Ein)
            .Must(ein => ProviderKeys.TryNormalizeEinFilter(ein, out _))
            .When(query => !string.IsNullOrWhiteSpace(query.Ein))
            .WithMessage("An EIN is 9 digits, for example 11-3471596.");

        RuleFor(query => query.Setting)
            .Must(setting => catalog.Setting(setting!) is not null)
            .When(query => !string.IsNullOrWhiteSpace(query.Setting))
            .WithMessage($"Setting must be one of: {string.Join(", ", catalog.Settings.Select(s => s.Key))}.");

        RuleFor(query => query.FeeType)
            .Must(feeType => feeType is "facility" or "professional")
            .When(query => !string.IsNullOrWhiteSpace(query.FeeType))
            .WithMessage("Fee type is either 'facility' or 'professional'.");

        RuleFor(query => query.Provider)
            .Must(name => name!.Trim().Length is >= 2 and <= 100)
            .When(query => !string.IsNullOrWhiteSpace(query.Provider))
            .WithMessage("Type at least 2 characters of the provider's name.");

        RuleFor(query => query.Sort)
            .Must(sort => Sorts.Contains(sort))
            .When(query => query.Sort is not null)
            .WithMessage($"Sort must be one of: {string.Join(", ", Sorts)}.");

        RuleFor(query => query.Snapshot).GreaterThan(0).When(query => query.Snapshot is not null)
            .WithMessage("Snapshot id must be a positive number.");
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1).WithMessage("Page starts at 1.");
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100).WithMessage("Page size is between 1 and 100.");
    }
}
