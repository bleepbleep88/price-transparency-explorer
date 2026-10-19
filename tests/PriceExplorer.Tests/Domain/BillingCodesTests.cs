using PriceExplorer.Api.Core.Domain;

namespace PriceExplorer.Tests.Domain;

public sealed class BillingCodesTests
{
    [Theory]
    [InlineData("CPT", "812", "00812")]       // anesthesia code published without its leading zeros
    [InlineData("CPT", "1991", "01991")]
    [InlineData("CPT", "45378", "45378")]
    [InlineData("CPT", "0232T", "0232T")]     // Category III codes keep their letter
    [InlineData("RC", "490", "0490")]         // revenue codes are four digits
    [InlineData("RC", "0490", "0490")]
    [InlineData("HCPCS", " j0585 ", "J0585")]
    [InlineData("MS-DRG", "5", "005")]
    public void Codes_are_trimmed_upper_cased_and_get_their_leading_zeros_back(string type, string raw, string expected)
    {
        BillingCodes.Canonicalize(type, raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("29881", "CPT", "29881")]
    [InlineData(" 45378 ", "CPT", "45378")]
    [InlineData("J0585", "HCPCS", "J0585")]
    [InlineData("0232T", "CPT", "0232T")]
    public void A_whole_procedure_code_in_free_text_is_recognized(string text, string expectedType, string expectedCode)
    {
        BillingCodes.TryParseProcedureCode(text, out var type, out var code).Should().BeTrue();
        type.Should().Be(expectedType);
        code.Should().Be(expectedCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2988")]
    [InlineData("29881 and 29880")]
    [InlineData("Rate is per visit")]
    [InlineData("Z1234")]
    public void Anything_else_in_free_text_is_left_alone(string? text)
    {
        BillingCodes.TryParseProcedureCode(text, out _, out _).Should().BeFalse();
    }
}
