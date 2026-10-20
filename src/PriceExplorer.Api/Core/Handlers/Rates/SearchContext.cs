using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Queries.Rates;

namespace PriceExplorer.Api.Core.Handlers.Rates;

/// <summary>A search request's inputs, parsed and normalized once and shared by the query, the assembler and the hints.</summary>
/// <param name="Code">The code as searched: "CPT 99213" becomes "99213", with "CPT" as <paramref name="CodeType"/>.</param>
/// <param name="Candidates">Every stored form the code could mean ("490" → 490, 00490, 0490).</param>
/// <param name="NameTyped">The provider name filter as typed (hints quote it this way).</param>
/// <param name="SpecialtyTyped">The specialty filter as typed.</param>
/// <param name="CountyTyped">The location filter (a New York county or NYC borough) as typed.</param>
internal sealed record SearchContext(
    string Code,
    string? CodeType,
    IReadOnlyList<string> Candidates,
    long? Npi,
    string? Ein,
    string? NameTyped,
    string? SpecialtyTyped,
    string? CountyTyped,
    string? Product,
    string? Setting,
    string? FeeType,
    string? Group)
{
    /// <summary>The name filter as the SQL compares it (lower case).</summary>
    public string? Name => NameTyped?.ToLowerInvariant();

    /// <summary>The specialty filter as the SQL compares it (lower case).</summary>
    public string? Specialty => SpecialtyTyped?.ToLowerInvariant();

    /// <summary>The location filter as the SQL compares it (lower case).</summary>
    public string? County => CountyTyped?.ToLowerInvariant();

    public bool IdFilter => Npi is not null || Ein is not null;

    /// <summary>A specialty or location filter: both pick providers by the NPIs they list (provider directory).</summary>
    public bool DirectoryFilter => SpecialtyTyped is not null || CountyTyped is not null;

    /// <summary>
    /// Any filter that picks providers: rows then show only matched rates, and groups highlight the matches. Statistics
    /// stay market-wide.
    /// </summary>
    public bool ProviderFilterActive => IdFilter || NameTyped is not null || DirectoryFilter;

    /// <summary>
    /// A code type with its own numbering (a DRG, an APC): these files only hold CPT, HCPCS and revenue codes, so equal
    /// digits are a coincidence. MS-DRG 0470 is a hospital stay; revenue code 0470 is audiology.
    /// </summary>
    public bool ForeignCodeType => CodeType is not (null or BillingCodes.Cpt or BillingCodes.Hcpcs or BillingCodes.RevenueCode);

    /// <summary>The NPI as typed: all 10 digits, leading zeros kept.</summary>
    public string? NpiText => Npi?.ToString("D10", System.Globalization.CultureInfo.InvariantCulture);

    public static SearchContext From(SearchRatesQuery request)
    {
        var (code, codeType) = CodeInput.Parse(request.Code, request.CodeType);
        return new SearchContext(
            code, codeType, CodeInput.Candidates(code, codeType), Domain.Npi.Parse(request.Npi), ProviderKeys.NormalizeKey(request.Ein),
            Blank(request.Provider), Blank(request.Specialty), Blank(request.County),
            Blank(request.Product), Blank(request.Setting), Blank(request.FeeType), Blank(request.Group));
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
