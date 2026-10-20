using PriceExplorer.Api.Core.Domain;

namespace PriceExplorer.Tests.Insights;

public sealed class InsightMathTests
{
    [Fact]
    public void The_price_index_is_the_median_of_the_groups_times_median_values()
    {
        InsightMath.PriceIndex([0.80m, 1.00m, 0.55m, 1.00m, 0.87m]).Should().Be(0.87m);
    }

    [Fact]
    public void An_even_number_of_groups_takes_the_midpoint_rounded_to_cents_like_every_group_statistic()
    {
        // 0.90 and 0.95 in the middle: 0.925 rounds half away from zero.
        InsightMath.PriceIndex([0.80m, 0.90m, 0.95m, 1.10m, 1.20m, 0.70m]).Should().Be(0.93m);
    }

    [Fact]
    public void Groups_without_a_median_are_left_out_and_fewer_than_five_give_no_index()
    {
        InsightMath.PriceIndex([0.80m, null, 1.00m, null, 0.90m, 1.10m]).Should().BeNull("only 4 groups have a median");
        InsightMath.PriceIndex([0.80m, null, 1.00m, 0.70m, 0.90m, 1.10m]).Should().Be(0.90m);
        InsightMath.PriceIndex([]).Should().BeNull();
    }

    [Theory]
    [InlineData("0.25", "half-or-less")]
    [InlineData("0.5", "half-or-less")]
    [InlineData("0.5001", "lower-25-50")]
    [InlineData("0.75", "lower-25-50")]
    [InlineData("0.8", "lower-10-25")]
    [InlineData("0.9", "lower-10-25")]
    [InlineData("0.95", "lower-under-10")]
    [InlineData("0.9999", "lower-under-10")]
    [InlineData("1", "same")]
    [InlineData("1.0000", "same")]
    [InlineData("1.0001", "higher-under-10")]
    [InlineData("1.1", "higher-10-25")]
    [InlineData("1.25", "higher-25-50")]
    [InlineData("1.5", "higher-50-100")]
    [InlineData("1.9999", "higher-50-100")]
    [InlineData("2", "double-or-more")]
    [InlineData("25", "double-or-more")]
    public void A_ratio_falls_in_one_band_and_an_edge_belongs_to_the_band_further_from_one(string ratio, string band)
    {
        InsightMath.BandOf(decimal.Parse(ratio, System.Globalization.CultureInfo.InvariantCulture)).Should().Be(band);
    }

    [Fact]
    public void Bands_run_from_lowest_to_highest_and_cover_every_ratio()
    {
        InsightMath.Bands.Select(band => band.Key).Should().OnlyHaveUniqueItems().And.HaveCount(10);
        InsightMath.Bands.Select(band => band.Direction).Should().Equal(
            "lower", "lower", "lower", "lower", "same", "higher", "higher", "higher", "higher", "higher");

        var keys = InsightMath.Bands.Select(band => band.Key).ToList();
        var sweep = Enumerable.Range(1, 400).Select(step => step / 100m).Select(InsightMath.BandOf).ToList();
        sweep.Should().OnlyContain(key => keys.Contains(key));
        sweep.Select(key => keys.IndexOf(key)).Should().BeInAscendingOrder("a higher ratio never lands in a lower band");
    }

    [Fact]
    public void Two_rates_are_as_far_apart_whichever_way_round_they_come()
    {
        InsightMath.Distance(50m, 100m).Should().Be(InsightMath.Distance(100m, 50m)).And.Be(2m);
        InsightMath.Distance(80m, 80m).Should().Be(1m);
        // A mirror-image pair from the real files: inverting a rounded ratio made these differ, so their tie-break was skipped.
        InsightMath.Distance(94.5m, 136.91m).Should().Be(InsightMath.Distance(136.91m, 94.5m));
        InsightMath.Distance(90m, 100m).Should().BeGreaterThan(InsightMath.Distance(110m, 100m), "10% lower is 1.11× apart, 10% higher 1.10×");
    }

    [Fact]
    public void A_ratio_needs_a_rate_above_zero_to_compare_with()
    {
        InsightMath.ExactRatio(50m, 100m).Should().Be(0.5m);
        InsightMath.ExactRatio(50m, 0m).Should().BeNull();
    }
}
