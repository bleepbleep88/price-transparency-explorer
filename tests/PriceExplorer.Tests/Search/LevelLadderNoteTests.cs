using PriceExplorer.Api.Core.Handlers.Rates;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Models.Statistics;

namespace PriceExplorer.Tests.Search;

/// <summary>
/// The level ladder's note (GetLevelLadderHandler.DipNote): a higher level's typical price below a lower level's is
/// explained when different places are priced at the two levels, as on 99213 for the Essential Plan in the office
/// (level 5 $101 from 70 places, level 4 $163 from 40).
/// </summary>
public sealed class LevelLadderNoteTests
{
    [Fact]
    public void A_dip_priced_by_different_places_says_so()
    {
        var levels = new[] { Level("99213", 113.59m, "a", "b", "c"), Level("99214", 163m, "a", "b"), Level("99215", 101m, "c", "d", "e") };

        GetLevelLadderHandler.DipNote(levels).Should().Be("Different places are priced at each level, so the steps don't always rise.");
    }

    [Fact]
    public void Rising_steps_or_the_same_places_paid_less_get_no_note()
    {
        GetLevelLadderHandler.DipNote([Level("99212", 60m, "a"), Level("99213", 114m, "b"), Level("99214", 163m, "c")]).Should().BeNull();
        GetLevelLadderHandler.DipNote([Level("99214", 163m, "a", "b"), Level("99215", 150m, "b", "a")])
            .Should().BeNull("the same places are paid less for more: that's not about who is priced");
    }

    [Fact]
    public void A_level_too_small_for_a_typical_price_is_left_out()
    {
        GetLevelLadderHandler.DipNote([Level("99214", 163m, "a"), Level("99215", null, "b")]).Should().BeNull();
    }

    private static GroupStats Level(string code, decimal? typical, params string[] places) => new(
        $"essential-plan|CPT|{code}|professional|office||negotiated", "essential-plan", "Essential Plan", "CPT", code, "professional",
        "office", "", "negotiated", "per visit", false, places.Length, places.Length, typical is null ? "list" : "basic",
        new RateStats(typical ?? 1m, null, null, typical, null, null, typical ?? 1m, null, null), null,
        places.Select(place => new ProviderValue(place, null, place, false, false, place, typical ?? 1m, typical ?? 1m, typical ?? 1m, 1, 1, 1, null, "")).ToList());
}
