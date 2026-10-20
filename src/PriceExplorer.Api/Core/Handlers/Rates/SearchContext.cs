using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Queries.Rates;

namespace PriceExplorer.Api.Core.Handlers.Rates;

/// <summary>A search request's inputs, parsed and normalized once and shared by the query, the assembler and the hints.</summary>
/// <param name="Code">The code as searched: "CPT 99213" becomes "99213", with "CPT" as <paramref name="CodeType"/>.</param>
/// <param name="Candidates">Every stored form the code could mean ("490" → 490, 00490, 0490).</param>
/// <param name="NameTyped">The provider name filter as typed (hints quote it this way).</param>
internal sealed record SearchContext(
    string Code,
    string? CodeType,
    IReadOnlyList<string> Candidates,
    long? Npi,
    string? Ein,
    string? NameTyped,
    string? Product,
    string? Setting,
    string? FeeType,
    string? Group)
{
    /// <summary>The name filter as the SQL compares it (lower case).</summary>
    public string? Name => NameTyped?.ToLowerInvariant();

    public bool IdFilter => Npi is not null || Ein is not null;

    public bool ProviderFilterActive => IdFilter || NameTyped is not null;

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
        ProviderKeys.TryNormalizeEinFilter(request.Ein, out var ein);
        return new SearchContext(
            code, codeType, CodeInput.Candidates(code, codeType), Domain.Npi.Parse(request.Npi), ein, Blank(request.Provider),
            Blank(request.Product), Blank(request.Setting), Blank(request.FeeType), Blank(request.Group));
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
