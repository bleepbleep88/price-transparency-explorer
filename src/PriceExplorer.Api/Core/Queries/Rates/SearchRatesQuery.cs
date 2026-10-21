using FluentValidation;
using MediatR;
using PriceExplorer.Api.Core.Behaviors;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Api.Core.Queries.Rates;

/// <summary>
/// Negotiated rates for one billing code, optionally narrowed to a provider by NPI, EIN, or name, or to providers with a
/// clinician specialty or a New York location (from the national provider registry).
/// </summary>
/// <param name="Code">Required. CPT/HCPCS/revenue code as typed ("45378", "j0585", "490").</param>
/// <param name="CodeType">Optional: CPT, HCPCS, RC... Needed only when a short number is ambiguous.</param>
/// <param name="Npi">Optional 10-digit NPI. Combined with an EIN, it must bill under that EIN.</param>
/// <param name="Ein">Optional 9-digit EIN (dash optional), or a provider key from a previous result.</param>
/// <param name="Product">Optional product key, e.g. "essential-plan".</param>
/// <param name="Setting">Optional care setting key, e.g. "asc", "hospital-outpatient", "office".</param>
/// <param name="FeeType">Optional: "facility" or "professional".</param>
/// <param name="Provider">Optional text matched against provider business names.</param>
/// <param name="Specialty">Optional: providers listing an NPI with this specialty. A full specialty name ("Urology") matches that specialty only; other text matches names with a word starting with each typed word ("gastro", "cardiologist"); a taxonomy code matches that code.</param>
/// <param name="County">Optional: providers listing an NPI that practices in this New York county or NYC borough ("Manhattan", "The Bronx", "Kings County", "Monroe", a FIPS code, or "NYC" for all five boroughs).</param>
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
    string? Specialty = null,
    string? County = null,
    string? Group = null,
    int? Snapshot = null,
    string? Sort = null,
    int Page = 1,
    int PageSize = 25) : IRequest<RateSearchResult>, IRequiresData;

public sealed partial class SearchRatesQueryValidator : AbstractValidator<SearchRatesQuery>
{
    public static readonly string[] Sorts = ["rate", "-rate", "provider", "-provider", "vsMedian", "-vsMedian"];

    /// <summary>Far beyond any real result set; keeps page arithmetic well inside integer range.</summary>
    public const int MaxPage = 100_000;

    // Code types allowed by the CMS in-network schema.
    public static readonly string[] CodeTypes =
        ["CPT", "HCPCS", "RC", "NDC", "ICD", "MS-DRG", "R-DRG", "S-DRG", "APS-DRG", "AP-DRG", "APR-DRG", "APC", "LOCAL", "EAPG", "HIPPS", "CDT", "CSTM-ALL"];

    public SearchRatesQueryValidator(ReferenceCatalog catalog)
    {
        // Two separate rules: a trailing When() applies to every check in its chain, which would disable "required".
        // A leading code-type word ("CPT 99213", "rc 0490") is allowed; the code after it is what's checked.
        RuleFor(query => query.Code)
            .Must(code => CodeInput.Clean(code).Length > 0)
            .WithMessage("Enter a billing code, for example 45378 (colonoscopy) or 0490 (ambulatory surgery).");

        RuleFor(query => query)
            .Must(query => CodeInput.Parse(query.Code, query.CodeType).Code is { Length: > 0 and <= 8 } clean && clean.All(char.IsAsciiLetterOrDigit))
            .When(query => CodeInput.Clean(query.Code).Length > 0)
            .OverridePropertyName(nameof(SearchRatesQuery.Code))
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
            .Must(InputRules.IsProviderKey)
            .When(query => !string.IsNullOrWhiteSpace(query.Ein))
            .WithMessage("Enter a 9-digit EIN, for example 11-3471596, or a provider key from search results.");

        RuleFor(query => query.Setting)
            .Must(setting => catalog.Setting(setting!) is not null)
            .When(query => !string.IsNullOrWhiteSpace(query.Setting))
            .WithMessage($"Setting must be one of: {string.Join(", ", catalog.Settings.Select(s => s.Key))}.");

        RuleFor(query => query.FeeType)
            .Must(feeType => feeType is "facility" or "professional")
            .When(query => !string.IsNullOrWhiteSpace(query.FeeType))
            .WithMessage("Fee type is either 'facility' or 'professional'.");

        RuleFor(query => query.Provider)
            .Must(name => name!.Trim().Length >= 2)
            .When(query => !string.IsNullOrWhiteSpace(query.Provider))
            .WithMessage("Type at least 2 characters of the provider's name.");

        RuleFor(query => query.Provider)
            .Must(name => name!.Trim().Length <= 100)
            .When(query => !string.IsNullOrWhiteSpace(query.Provider))
            .WithMessage("A provider name is at most 100 characters.");

        // A separate rule so whitespace-only values with control characters (" \0 ") are caught too.
        RuleFor(query => query.Provider)
            .Must(name => !InputRules.HasControlCharacters(name!))
            .When(query => query.Provider is not null)
            .WithMessage("The provider name can't contain control characters.");

        RuleFor(query => query.Specialty)
            .Must(specialty => specialty!.Trim().Length >= 2)
            .When(query => !string.IsNullOrWhiteSpace(query.Specialty))
            .WithMessage("Type at least 2 characters of a specialty, for example 'gastro'.");

        RuleFor(query => query.Specialty)
            .Must(specialty => specialty!.Trim().Length <= 100)
            .When(query => !string.IsNullOrWhiteSpace(query.Specialty))
            .WithMessage("A specialty is at most 100 characters.");

        RuleFor(query => query.County)
            .Must(county => county!.Trim().Length >= 2)
            .When(query => !string.IsNullOrWhiteSpace(query.County))
            .WithMessage("Enter a New York county or NYC borough, for example 'Brooklyn' or 'Monroe'.");

        RuleFor(query => query.County)
            .Must(county => county!.Trim().Length <= 60)
            .When(query => !string.IsNullOrWhiteSpace(query.County))
            .WithMessage("A location is at most 60 characters.");

        RuleFor(query => query.Specialty)
            .Must(specialty => !InputRules.HasControlCharacters(specialty!))
            .When(query => query.Specialty is not null)
            .WithMessage("The specialty can't contain control characters.");

        RuleFor(query => query.County)
            .Must(county => !InputRules.HasControlCharacters(county!))
            .When(query => query.County is not null)
            .WithMessage("The location can't contain control characters.");

        // Product keys are slugs from the snapshot ("essential-plan"); group keys come from search results.
        RuleFor(query => query.Product)
            .Must(product => IsProductKey(product!))
            .When(query => !string.IsNullOrWhiteSpace(query.Product))
            .WithMessage("A product is a product key from the snapshot, for example essential-plan.");

        RuleFor(query => query.Group)
            .Must(group => group!.Length <= 300 && !InputRules.HasControlCharacters(group) && group.Split('|').Length == 7)
            .When(query => !string.IsNullOrWhiteSpace(query.Group))
            .WithMessage("A group is a comparison group key from search results.");

        RuleFor(query => query.Product)
            .Must(product => !InputRules.HasControlCharacters(product!))
            .When(query => query.Product is not null)
            .WithMessage("The product can't contain control characters.");

        RuleFor(query => query.Sort)
            .Must(sort => Sorts.Contains(sort))
            .When(query => query.Sort is not null)
            .WithMessage($"Sort must be one of: {string.Join(", ", Sorts)}.");

        RuleFor(query => query.Snapshot).GreaterThan(0).When(query => query.Snapshot is not null)
            .WithMessage("Snapshot id must be a positive number.");
        RuleFor(query => query.Page).InclusiveBetween(1, MaxPage).WithMessage($"Page is between 1 and {MaxPage:N0}.");
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100).WithMessage("Page size is between 1 and 100.");
    }

    /// <summary>The shape of a product key from the snapshot ("essential-plan"); the insights filters use it too.</summary>
    internal static bool IsProductKey(string product) => ProductKey().IsMatch(product.Trim());

    [System.Text.RegularExpressions.GeneratedRegex("^[a-z0-9][a-z0-9-]{0,127}$")]
    private static partial System.Text.RegularExpressions.Regex ProductKey();
}
