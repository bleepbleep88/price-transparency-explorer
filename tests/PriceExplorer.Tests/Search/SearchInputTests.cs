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

    [Theory]
    [InlineData("CPT 99213", null, "99213", "CPT")]
    [InlineData("rc 0490", null, "0490", "RC")]
    [InlineData("RC0490", null, "0490", "RC")]
    [InlineData("Revenue code 450", null, "450", "RC")]
    [InlineData("hcpcs j0585", null, "J0585", "HCPCS")]
    [InlineData("CPT 99213", "HCPCS", "99213", "HCPCS")]   // an explicit code type wins over the typed word
    [InlineData("C9399", null, "C9399", null)]             // a real HCPCS code, not a "CPT" prefix
    [InlineData(" 45378 ", null, "45378", null)]
    public void A_leading_code_type_word_becomes_the_code_type(string input, string? codeType, string code, string? expectedType)
    {
        CodeInput.Parse(input, codeType).Should().Be((code, expectedType));
        _validator.TestValidate(new SearchRatesQuery(input, CodeType: codeType)).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("HCPCS", "HCPCS")]
    [InlineData("CPTS", "CPTS")]
    [InlineData("RCA", "RCA")]
    [InlineData("Revenue", "REVENUE")]
    [InlineData("rev code", "REVCODE")]
    public void A_bare_type_word_is_kept_as_typed_not_split_into_a_fake_code(string input, string code)
    {
        CodeInput.Parse(input, null).Should().Be((code, (string?)null));
    }

    [Theory]
    [InlineData("CPT00812", "00812", "CPT")]
    [InlineData("cpt 0001U", "0001U", "CPT")]
    [InlineData("CPT 3074F", "3074F", "CPT")]
    [InlineData("HCPCS: J0585", "J0585", "HCPCS")]
    [InlineData("rev code 0490", "0490", "RC")]
    public void Codes_after_a_type_word_keep_parsing(string input, string code, string type)
    {
        CodeInput.Parse(input, null).Should().Be((code, (string?)type));
    }

    [Fact]
    public void Control_characters_in_free_text_are_rejected_before_they_reach_sql()
    {
        _validator.TestValidate(new SearchRatesQuery("99213", Provider: "\0\0")).ShouldHaveValidationErrorFor(q => q.Provider);
        _validator.TestValidate(new SearchRatesQuery("99213", Provider: "a\0b")).ShouldHaveValidationErrorFor(q => q.Provider);
        new SuggestCodesQueryValidator().TestValidate(new SuggestCodesQuery("\0")).ShouldHaveValidationErrorFor(q => q.Q);
        new SuggestProvidersQueryValidator().TestValidate(new SuggestProvidersQuery("\0\0")).ShouldHaveValidationErrorFor(q => q.Q);
    }

    [Fact]
    public void The_npi_list_takes_every_rate_behind_the_largest_row_and_reports_the_limit_under_its_parameter_name()
    {
        var validator = new ListRateNpisQueryValidator();

        validator.TestValidate(new ListRateNpisQuery(1, AlsoRateIds: Enumerable.Range(2, 59).Select(i => (long)i).ToList()))
            .ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new ListRateNpisQuery(1, AlsoRateIds: Enumerable.Range(2, 301).Select(i => (long)i).ToList()))
            .ShouldHaveValidationErrorFor("alsoRate");
    }

    [Fact]
    public void Statistics_round_half_cents_up_like_sql_round()
    {
        RateStatistics.Median([321.18m, 331.15m]).Should().Be(326.17m, "326.165 rounds up, as DuckDB round() and Excel ROUND do");
        RateStatistics.Ratio(34.65m, 42.00m).Should().Be(0.83m, "0.825 rounds up");
        RateStatistics.Median([1.00m, 1.01m]).Should().Be(1.01m);
    }

    [Theory]
    [InlineData("J0585", "drugs", true)]
    [InlineData("Q0138", "drugs", true)]    // ferumoxytol
    [InlineData("Q5101", "drugs", true)]    // a biosimilar
    [InlineData("Q9967", "drugs", true)]    // contrast, per ml
    [InlineData("Q4100", "supplies", true)] // skin substitute
    [InlineData("Q0091", "other", false)]   // Pap smear collection: a service, not a drug
    public void Hcpcs_q_codes_split_into_drugs_supplies_and_services(string code, string key, bool perUnit)
    {
        var category = ServiceCategories.Classify("HCPCS", code);

        category.Key.Should().Be(key);
        category.PerUnit.Should().Be(perUnit);
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
    public void Pages_far_beyond_any_result_are_rejected_instead_of_overflowing()
    {
        _validator.TestValidate(new SearchRatesQuery("45378", Page: 100_000_001)).ShouldHaveValidationErrorFor(q => q.Page);
        _validator.TestValidate(new SearchRatesQuery("45378", Page: 100_000)).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(new[] { 100.0, 140.0 }, new[] { 1, 1 }, 100.0)]          // tie: the lower rate, never the $120 midpoint
    [InlineData(new[] { 100.0, 140.0 }, new[] { 1, 3 }, 140.0)]          // most NPIs are paid $140
    [InlineData(new[] { 100.0, 120.0, 140.0 }, new[] { 5, 3, 4 }, 120.0)] // weighted median, not the most common
    [InlineData(new[] { 90.0 }, new[] { 0 }, 90.0)]                      // a whole-tax-ID contract lists no NPIs
    public void A_providers_several_rates_reduce_to_one_it_is_really_paid(double[] rates, int[] npis, double expected)
    {
        RateStatistics.WeightedMedian(rates.Zip(npis, (rate, weight) => ((decimal)rate, weight))).Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData(167.09, "typical")]   // at the median of a fee schedule: not "low"
    [InlineData(150.00, "low")]
    [InlineData(185.40, "high")]
    public void Bands_never_call_the_median_low_or_high(double value, string expected)
    {
        // A fee schedule: 25th percentile = median = 75th percentile.
        RateStatistics.Band((decimal)value, 167.09m, 167.09m, 167.09m).Should().Be(expected);
        RateStatistics.Band(300m, 300m, 400m, 500m).Should().Be("low", "at the 25th percentile and under the median");
        RateStatistics.Band(500m, 300m, 400m, 500m).Should().Be("high");
        RateStatistics.Band(400m, 300m, 400m, 500m).Should().Be("typical");
    }

    [Fact]
    public void A_matched_providers_percentile_leaves_out_its_own_market_value()
    {
        decimal[] market = [45m, 60m, 67.5m, 70m, 80m];   // 67.50 is the provider's own blended value

        RateStatistics.PercentRank(market, 45m, own: 67.5m).Should().Be(13, "one of the 4 others is also paid $45 and counts half");
        RateStatistics.PercentRank(market, 90m, own: 67.5m).Should().Be(100, "everyone else is below 90; its own 67.50 isn't counted");
    }

    [Fact]
    public void A_provider_paid_the_standard_rate_sits_in_the_middle_not_at_the_bottom()
    {
        // A fee schedule: 49 of 51 providers paid $167.09.
        decimal[] market = [143.75m, .. Enumerable.Repeat(167.09m, 49), 185.40m];

        RateStatistics.PercentRank(market, 167.09m).Should().Be(50, "1 below, 48 others at the same rate count half: (1 + 24) / 50");
        RateStatistics.PercentRank(market, 143.75m).Should().Be(0);
        RateStatistics.PercentRank(market, 185.40m).Should().Be(100);
    }

    [Fact]
    public void A_half_cent_quantile_rounds_up_even_where_floating_point_lands_just_below()
    {
        // 3.79 + 0.1 × 12.25 = 5.015 exactly; DuckDB's quantile_cont gets 5.0149 because it interpolates in floating point.
        RateStatistics.Quantile([1.52m, 1.52m, 1.52m, 1.52m, 1.52m, 1.52m, 1.52m, 3.41m, 3.79m, 16.04m], 0.9).Should().Be(5.02m);
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
