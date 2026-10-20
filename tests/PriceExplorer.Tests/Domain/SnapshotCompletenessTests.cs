using PriceExplorer.Api.Core.Domain;

namespace PriceExplorer.Tests.Domain;

public sealed class SnapshotCompletenessTests
{
    private static ProductCounts Counts(int snapshot, string date, long rates, long eins = 100, long codes = 50, string product = "ambetter",
        bool readFully = true) =>
        new(snapshot, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), product, readFully, rates, eins, codes);

    [Fact]
    public void A_lone_snapshot_has_nothing_to_compare_with_so_it_isnt_called_partial()
    {
        var score = SnapshotCompleteness.Score([Counts(1, "2026-08-31", rates: 10)])[(1, "ambetter")];

        score.Should().BeEquivalentTo(new ProductCompleteness(null, null, null, null, [], Partial: false));
    }

    [Fact]
    public void A_partial_upload_is_flagged_and_its_neighbours_are_not()
    {
        // Centene's Ambetter file: about 7,700 unique rates in full months, a few hundred in a partial upload.
        var scores = SnapshotCompleteness.Score(
        [
            Counts(1, "2025-11-24", rates: 7_600), Counts(2, "2025-12-29", rates: 400, eins: 30, codes: 40), Counts(3, "2026-01-26", rates: 7_700),
        ]);

        scores[(2, "ambetter")].Should().BeEquivalentTo(new ProductCompleteness(0.05m, 0.05m, 0.3m, 0.8m, [1, 3], Partial: true));
        scores[(1, "ambetter")].Partial.Should().BeFalse("the partial neighbour only lowers the median it's compared with");
        scores[(3, "ambetter")].Partial.Should().BeFalse();
    }

    [Theory]
    [InlineData(80, false)]
    [InlineData(79.5, false)] // 0.795 shows as 0.80, so it isn't flagged either
    [InlineData(79.4, true)]
    public void The_threshold_is_80_percent_of_the_nearby_median_as_shown(double percent, bool partial)
    {
        var scores = SnapshotCompleteness.Score([Counts(1, "2026-08-31", rates: 1_000), Counts(2, "2026-09-28", rates: (long)(percent * 10))]);

        scores[(2, "ambetter")].Partial.Should().Be(partial);
        scores[(2, "ambetter")].Score.Should().Be(decimal.Round((decimal)percent / 100, 2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void Any_one_measure_short_of_its_neighbours_flags_the_file()
    {
        var scores = SnapshotCompleteness.Score(
            [Counts(1, "2026-07-27", rates: 100, eins: 100), Counts(2, "2026-08-31", rates: 100, eins: 50), Counts(3, "2026-09-28", rates: 100, eins: 100)]);

        scores[(2, "ambetter")].Should().BeEquivalentTo(new ProductCompleteness(0.5m, 1m, 0.5m, 1m, [1, 3], Partial: true));
    }

    [Fact]
    public void Only_two_snapshots_on_each_side_count_since_files_grow_over_the_months()
    {
        var scores = SnapshotCompleteness.Score(
        [
            Counts(1, "2025-09-01", rates: 1_000), Counts(2, "2025-10-01", rates: 1_000), Counts(3, "2025-11-01", rates: 9_000),
            Counts(4, "2025-12-01", rates: 9_000), Counts(5, "2026-01-01", rates: 9_000), Counts(6, "2026-02-01", rates: 9_000),
        ]);

        scores[(1, "ambetter")].ComparedWith.Should().Equal(2, 3);
        scores[(4, "ambetter")].ComparedWith.Should().Equal(2, 3, 5, 6);
        scores[(4, "ambetter")].Rates.Should().Be(1m, "the median of 1,000, 9,000, 9,000 and 9,000 is 9,000");
        scores[(6, "ambetter")].Should().BeEquivalentTo(new ProductCompleteness(1m, 1m, 1m, 1m, [4, 5], Partial: false));
    }

    [Fact]
    public void Snapshots_are_ordered_by_index_date_not_by_id()
    {
        // Ids are given out in load order, newest first.
        var scores = SnapshotCompleteness.Score([Counts(1, "2026-09-28", rates: 100), Counts(2, "2026-08-31", rates: 100), Counts(3, "2026-07-27", rates: 100)]);

        scores[(1, "ambetter")].ComparedWith.Should().Equal(3, 2);
        scores[(3, "ambetter")].ComparedWith.Should().Equal(2, 1);
    }

    [Fact]
    public void An_even_number_of_neighbours_takes_the_middle_two()
    {
        SnapshotCompleteness.Median([1, 7]).Should().Be(4m);
        SnapshotCompleteness.Median([9, 1, 7]).Should().Be(7m);
    }

    [Fact]
    public void A_file_read_only_in_part_is_partial_even_when_its_counts_look_complete()
    {
        var scores = SnapshotCompleteness.Score([Counts(1, "2026-08-31", rates: 100), Counts(2, "2026-09-28", rates: 100, readFully: false)]);

        scores[(2, "ambetter")].Score.Should().Be(1m);
        scores[(2, "ambetter")].Partial.Should().BeTrue();
    }

    [Fact]
    public void A_measure_whose_neighbours_have_none_is_left_out_of_the_score()
    {
        var scores = SnapshotCompleteness.Score([Counts(1, "2026-08-31", rates: 100, codes: 0), Counts(2, "2026-09-28", rates: 90, codes: 5)]);

        scores[(2, "ambetter")].Codes.Should().BeNull();
        scores[(2, "ambetter")].Score.Should().Be(0.9m);
    }

    [Fact]
    public void Each_product_is_compared_with_itself_only()
    {
        var scores = SnapshotCompleteness.Score(
        [
            Counts(1, "2026-08-31", rates: 7_700, product: "ambetter"), Counts(1, "2026-08-31", rates: 9_800, product: "essential"),
            Counts(2, "2026-09-28", rates: 7_700, product: "ambetter"), Counts(2, "2026-09-28", rates: 900, product: "essential"),
        ]);

        scores[(2, "ambetter")].Partial.Should().BeFalse();
        scores[(2, "essential")].Rates.Should().Be(0.09m);
        scores[(2, "essential")].Partial.Should().BeTrue();
    }
}
