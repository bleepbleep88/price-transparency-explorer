using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Handlers.Insights;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Tests.Insights;

public sealed class InsightSupportTests
{
    private static readonly ReferenceCatalog Catalog = new();

    [Fact]
    public void A_code_is_named_as_the_plan_names_it_and_a_revenue_code_by_the_label_search_shows()
    {
        var names = new Dictionary<(string CodeType, string Code), string> { [("CPT", "45378")] = "Colonoscopy" };

        InsightSupport.NameOf(names, Catalog, "CPT", "45378").Should().Be("Colonoscopy");
        InsightSupport.NameOf(names, Catalog, "RC", "0360").Should().Be("Operating room", "the 036x category's label");
        InsightSupport.NameOf(names, Catalog, "CPT", "99213").Should().BeNull("the files publish no name for it");
    }

    [Fact]
    public void A_search_link_opens_the_group_and_names_the_snapshot_only_when_it_isnt_the_default()
    {
        var latest = new ResolvedSnapshot(1, new DateOnly(2026, 8, 31), IsLatest: true);
        var earlier = new ResolvedSnapshot(2, new DateOnly(2026, 7, 27), IsLatest: false);

        InsightSupport.SearchLink(latest, "CPT", "45378", "g").Should().BeEquivalentTo(
            new Dictionary<string, string> { ["code"] = "45378", ["codeType"] = "CPT", ["group"] = "g" });
        InsightSupport.SearchLink(earlier, "RC", "0490", "g", "u0123456789ab").Should().BeEquivalentTo(
            new Dictionary<string, string> { ["code"] = "0490", ["codeType"] = "RC", ["group"] = "g", ["ein"] = "u0123456789ab", ["snapshot"] = "2" });
    }
}
