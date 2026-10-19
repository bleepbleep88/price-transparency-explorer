using PriceExplorer.Api.Core.Domain;

namespace PriceExplorer.Tests.Domain;

public sealed class ProviderIdsTests
{
    [Theory]
    [InlineData(1396747861L, true)]   // from the Fidelis file
    [InlineData(1234567893L, true)]   // CMS check-digit example
    [InlineData(1861171663L, false)]  // published in the Essential Plan file, fails the check digit
    [InlineData(124507857L, false)]   // nine digits
    [InlineData(0L, false)]           // CMS marker for "whole tax ID, no NPIs listed"
    [InlineData(3396747861L, false)]  // NPIs start with 1 or 2
    public void Npi_check_digit_follows_the_cms_luhn_rule(long npi, bool expected)
    {
        Npi.IsValid(npi).Should().Be(expected);
    }

    [Theory]
    [InlineData("1396747861", 1396747861L)]
    [InlineData(" 1396-747-861 ", 1396747861L)]
    [InlineData("139674786", null)]
    [InlineData("abc", null)]
    public void Npi_input_is_parsed_from_digits(string input, long? expected)
    {
        Npi.Parse(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("11-3471596", "113471596")]
    [InlineData("113471596", "113471596")]
    [InlineData("11 3471596", "113471596")]
    [InlineData("1134715", null)]
    [InlineData(null, null)]
    public void Ein_is_normalized_to_nine_digits(string? input, string? expected)
    {
        Ein.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Ein_prefixes_the_irs_never_issues_are_flagged_and_masked()
    {
        Ein.HasNeverIssuedPrefix("071234567").Should().BeTrue();
        Ein.HasNeverIssuedPrefix("113471596").Should().BeFalse();
        Ein.Mask("071234567").Should().Be("••-•••4567");
        Ein.Format("113471596").Should().Be("11-3471596");
    }
}
