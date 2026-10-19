using PriceExplorer.Api.Core.Ingestion;

namespace PriceExplorer.Tests.Ingestion;

public sealed class ProductNamingTests
{
    [Fact]
    public void The_shared_leading_words_of_plan_names_become_the_product_name_even_with_a_typo()
    {
        var plans = new List<string?>
        {
            "Ambetter from Fidelis Care Bronze",
            "Ambetter from Fidelis Care Silver 200",
            "Ambetter from Fidelis Care Gold Zero Cost Sharing",
            "Ambetter from Fidelis Care Platinum",
            "Ambetter fromFidelis Care Silver 250 Enhanced", // as published
        };

        ProductNaming.LabelFor(plans, "fallback").Should().Be("Ambetter from Fidelis Care");
    }

    [Fact]
    public void Numbered_variants_share_their_common_prefix()
    {
        ProductNaming.LabelFor(["Essential Plan 1", "Essential Plan 2", "Essential Plan 200-250"], "fallback")
            .Should().Be("Essential Plan");
    }

    [Fact]
    public void Without_plan_names_the_fallback_is_used()
    {
        ProductNaming.LabelFor([], "fidelis-ex_in-network").Should().Be("fidelis-ex_in-network");
    }

    [Theory]
    [InlineData("Ambetter from Fidelis Care", "ambetter-from-fidelis-care")]
    [InlineData("Essential Plan", "essential-plan")]
    [InlineData("  ***  ", "product")]
    public void Product_keys_are_url_friendly(string label, string expected)
    {
        ProductNaming.KeyFor(label).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://www.centene.com/content/dam/centene/Centene%20Corporate/json/DOCUMENT/2026-08-31_fidelis_index.json", "2026-08-31")]
    [InlineData("https://example.test/index.json", null)]
    public void The_snapshot_date_comes_from_the_index_file_name(string url, string? expected)
    {
        TableOfContentsReader.DateFromUrl(url).Should().Be(expected is null ? null : DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }
}
