using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Handlers.Insights;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Tests.Insights;

public sealed class InsightSupportTests
{
    private static readonly ReferenceCatalog Catalog = new();

    [Fact]
    public void A_HCPCS_code_is_named_as_the_plan_names_it_a_revenue_code_by_our_label_and_a_CPT_code_by_CMS_RBCS()
    {
        var codes = new CodeReference();
        codes.Set([
            ("45378", new RbcsClass("P", "Procedure", "PG", "Digestive/Gastrointestinal", "004", "Lower GI Endoscopy - Other")),
            ("00731", new RbcsClass("A", "Anesthesia", "AA", "Anesthesia", null, null)),
        ]);
        // The plan's text for a CPT code is never used, even if a saved database still held some (PublishedText).
        var names = new Dictionary<(string CodeType, string Code), string>
        {
            [("HCPCS", "J0585")] = "Botulinum toxin injection",
            [("CPT", "45378")] = "Service 45378",
        };

        InsightSupport.NameOf(names, Catalog, codes, "HCPCS", "J0585").Should().Be("Botulinum toxin injection");
        InsightSupport.NameOf(names, Catalog, codes, "RC", "0360").Should().Be("Operating room", "the 036x category's label");
        InsightSupport.NameOf(names, Catalog, codes, "CPT", "45378").Should().Be("Lower GI endoscopy");
        InsightSupport.NameOf(names, Catalog, codes, "CPT", "00731").Should().Be("Anesthesia");
        InsightSupport.NameOf(names, Catalog, codes, "CPT", "99213").Should().BeNull("RBCS doesn't list it here");
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
