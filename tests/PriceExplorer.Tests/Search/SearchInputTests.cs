using FluentValidation.TestHelper;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Queries.Rates;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Tests.Search;

public sealed class SearchInputTests
{
    private readonly SearchRatesQueryValidator _validator = new(new ReferenceCatalog());

    [Theory]
    [InlineData(" 45378 ", null, new[] { "45378" })]
    [InlineData("490", null, new[] { "490", "00490", "0490" })]   // revenue code 0490 or anesthesia-style CPT
    [InlineData("812", null, new[] { "812", "00812", "0812" })]
    [InlineData("j0585", null, new[] { "J0585" })]
    [InlineData("490", "RC", new[] { "0490" })]
    [InlineData("812", "CPT", new[] { "00812" })]
    public void Typed_codes_expand_to_every_stored_form_they_could_mean(string input, string? codeType, string[] expected)
    {
        CodeInput.Candidates(CodeInput.Clean(input), codeType).Should().Equal(expected);
    }

    [Fact]
    public void A_missing_code_is_rejected_with_a_helpful_message()
    {
        _validator.TestValidate(new SearchRatesQuery(Code: null))
            .ShouldHaveValidationErrorFor(q => q.Code)
            .WithErrorMessage("Enter a billing code, for example 45378 (colonoscopy) or 0490 (ambulatory surgery).");
    }

    [Theory]
    [InlineData("45378")]
    [InlineData("0490")]
    [InlineData("j0585")]
    [InlineData("0232T")]
    public void Valid_codes_pass(string code)
    {
        _validator.TestValidate(new SearchRatesQuery(code)).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("!!!")]
    [InlineData("123456789")]
    public void Malformed_codes_fail(string code)
    {
        _validator.TestValidate(new SearchRatesQuery(code)).ShouldHaveValidationErrorFor(q => q.Code);
    }

    [Theory]
    [InlineData("1396747861", true)]
    [InlineData("1396-747-861", true)]
    [InlineData("1861171663", true)]   // fails the check digit, but is a real published NPI: searched with a warning
    [InlineData("12345", false)]
    [InlineData("13967478AB", false)]
    public void Npi_must_be_ten_digits(string npi, bool valid)
    {
        var result = _validator.TestValidate(new SearchRatesQuery("45378", Npi: npi));
        if (valid) result.ShouldNotHaveValidationErrorFor(q => q.Npi);
        else result.ShouldHaveValidationErrorFor(q => q.Npi);
    }

    [Theory]
    [InlineData("11-3471596", true)]
    [InlineData("113471596", true)]
    [InlineData("u0123456789ab", true)]   // provider key for a masked tax ID
    [InlineData("1134715", false)]
    [InlineData("ab-cdefghi", false)]
    public void Ein_accepts_nine_digits_or_a_provider_key(string ein, bool valid)
    {
        var result = _validator.TestValidate(new SearchRatesQuery("45378", Ein: ein));
        if (valid) result.ShouldNotHaveValidationErrorFor(q => q.Ein);
        else result.ShouldHaveValidationErrorFor(q => q.Ein);
    }

    [Fact]
    public void Unknown_settings_sorts_and_fee_types_are_rejected()
    {
        var result = _validator.TestValidate(new SearchRatesQuery("45378", Setting: "moon", FeeType: "both", Sort: "price", Page: 0, PageSize: 500));

        result.ShouldHaveValidationErrorFor(q => q.Setting);
        result.ShouldHaveValidationErrorFor(q => q.FeeType);
        result.ShouldHaveValidationErrorFor(q => q.Sort);
        result.ShouldHaveValidationErrorFor(q => q.Page);
        result.ShouldHaveValidationErrorFor(q => q.PageSize);
    }

    [Fact]
    public void Quantiles_use_linear_interpolation()
    {
        decimal[] sorted = [100m, 200m, 300m, 400m];

        RateStatistics.Quantile(sorted, 0.25).Should().Be(175m);
        RateStatistics.Median(sorted).Should().Be(250m);
        RateStatistics.PercentRank(sorted, 300m).Should().Be(67);
        RateStatistics.LevelFor(4).Should().Be("list");
        RateStatistics.LevelFor(5).Should().Be("basic");
        RateStatistics.LevelFor(10).Should().Be("full");
    }

    [Fact]
    public void Every_reference_file_loads_without_duplicate_codes()
    {
        var catalog = new ReferenceCatalog();

        catalog.SettingForPlace("24").Key.Should().Be("asc");
        catalog.SettingForPlace("").Key.Should().Be("unspecified");
        catalog.SettingForPlace("98").Key.Should().Be("other");
        catalog.RevenueCodeLabel("0490").Should().Be("Ambulatory surgery");
        catalog.RevenueCodeLabel("0513").Should().Be("Clinic");
        catalog.Modifier("26").Component.Should().Be("professional");
        catalog.Modifier("ZZ").Label.Should().Be("Modifier ZZ");
    }
}
