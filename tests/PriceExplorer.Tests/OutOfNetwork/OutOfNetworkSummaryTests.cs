using PriceExplorer.Api.Core.Handlers.OutOfNetwork;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Tests.OutOfNetwork;

public sealed class OutOfNetworkSummaryTests
{
    private static readonly ReferenceCatalog Catalog = new();

    [Fact]
    public void Under_five_amounts_are_listed_lowest_first_with_no_median()
    {
        var summary = OutOfNetworkSummary.Amounts([544.80m, 217.92m, 8373.66m, 762.72m]);

        summary.Count.Should().Be(4);
        summary.Median.Should().BeNull();
        summary.Listed.Should().Equal(217.92m, 544.80m, 762.72m, 8373.66m);
        (summary.Min, summary.Max).Should().Be((217.92m, 8373.66m));
    }

    [Fact]
    public void From_five_amounts_the_median_interpolates_and_rounds_half_cents_up_like_the_rate_statistics()
    {
        var summary = OutOfNetworkSummary.Amounts([30m, 1m, 10.01m, 2m, 20m, 10m]);

        summary.Median.Should().Be(10.01m, "(10.00 + 10.01) / 2 = 10.005 rounds away from zero");
        summary.Listed.Should().BeEmpty();
        (summary.Min, summary.Max).Should().Be((1m, 30m));
    }

    [Fact]
    public void Facility_and_professional_amounts_are_never_mixed_and_modifiers_split_groups()
    {
        var groups = OutOfNetworkSummary.Summarize(
        [
            Fact(1, billingClass: "institutional", allowed: 100m),
            Fact(2, billingClass: "professional", allowed: 20m),
            Fact(3, billingClass: "professional", allowed: 25m, modifiers: "26"),
        ], Catalog);

        groups.Select(group => group.Key).Should().Equal(
            "essential-plan|CPT|74177|facility|", "essential-plan|CPT|74177|professional|", "essential-plan|CPT|74177|professional|26");
        groups[2].Modifiers.Should().ContainSingle().Which.Code.Should().Be("26");
    }

    [Fact]
    public void Billed_charges_count_once_per_provider_and_npis_once_per_group()
    {
        var groups = OutOfNetworkSummary.Summarize(
        [
            // Payment 1: one provider entry listing two NPIs, and a second provider with the first NPI again.
            Fact(1, allowed: 91.25m, providerIndex: 0, billed: 246m, npi: 1407814825),
            Fact(1, allowed: 91.25m, providerIndex: 0, billed: 246m, npi: 1538690458),
            Fact(1, allowed: 91.25m, providerIndex: 1, billed: 315m, npi: 1407814825),
            // Payment 2: no providers listed. Payment 3: a provider whose billed charge wasn't a number.
            Fact(2, allowed: 182.50m, providerIndex: null, billed: null, npi: null, providerKey: "161201211"),
            Fact(3, allowed: 273.75m, providerIndex: 0, billed: null, npi: 1730684499),
        ], Catalog);

        var group = groups.Should().ContainSingle().Subject;
        group.Payments.Should().Be(3);
        group.Providers.Should().Be(2);
        group.Npis.Should().Be(3);
        group.Allowed.Listed.Should().Equal(91.25m, 182.50m, 273.75m);
        group.Billed!.Listed.Should().Equal(246m, 315m);
    }

    [Fact]
    public void The_share_allowed_is_the_median_over_each_provider_on_a_payment_from_five_pairs()
    {
        // Allowed ÷ billed: 0.10, 0.20, 0.25, 0.40, 0.50, 1.00; the payment without a billed charge has no share.
        var six = OutOfNetworkSummary.Summarize(
        [
            Fact(1, allowed: 10m, billed: 100m),
            Fact(2, allowed: 20m, billed: 100m),
            Fact(3, allowed: 25m, billed: 100m),
            Fact(4, allowed: 40m, billed: 100m),
            Fact(5, allowed: 50m, providerIndex: 0, billed: 100m, npi: 1407814825),
            Fact(5, allowed: 50m, providerIndex: 1, billed: 50m, npi: 1538690458),
            Fact(6, allowed: 70m, providerIndex: null, billed: null, npi: null),
        ], Catalog).Single();
        var four = OutOfNetworkSummary.Summarize([Fact(1), Fact(2), Fact(3), Fact(4)], Catalog).Single();

        six.AllowedShareOfBilled.Should().Be(0.33m, "(0.25 + 0.40) / 2 = 0.325 rounds half away from zero");
        four.AllowedShareOfBilled.Should().BeNull();
    }

    [Fact]
    public void A_group_with_no_billed_charges_has_none_and_places_are_the_union_of_its_payments()
    {
        var groups = OutOfNetworkSummary.Summarize(
        [
            Fact(1, allowed: 10m, providerIndex: null, billed: null, npi: null, places: "22,23"),
            Fact(2, allowed: 11m, providerIndex: null, billed: null, npi: null, places: "19,23"),
        ], Catalog);

        var group = groups.Should().ContainSingle().Subject;
        group.Billed.Should().BeNull();
        group.AllowedShareOfBilled.Should().BeNull();
        group.Places.Select(place => place.Pos).Should().Equal("19", "22", "23");
        group.Places[0].Name.Should().Be("Off campus outpatient hospital");
    }

    [Fact]
    public void Codes_priced_per_unit_in_network_are_marked_and_groups_follow_code_then_product_order()
    {
        var groups = OutOfNetworkSummary.Summarize(
        [
            Fact(1, code: "00812", productKey: "essential-plan", structureIndex: 1),
            Fact(2, code: "00812", productKey: "ambetter", structureIndex: 0),
            Fact(3, codeType: "HCPCS", code: "A0425"),
            Fact(4, codeType: "RC", code: "0450", billingClass: "institutional"),
        ], Catalog);

        groups.Select(group => (group.CodeType, group.Code, group.ProductKey, group.PerUnitCode)).Should().Equal(
            ("CPT", "00812", "ambetter", true),
            ("CPT", "00812", "essential-plan", true),
            ("HCPCS", "A0425", "essential-plan", true),
            ("RC", "0450", "essential-plan", false));
    }

    private static OutOfNetworkFact Fact(
        long paymentId,
        string codeType = "CPT",
        string code = "74177",
        string billingClass = "professional",
        decimal allowed = 100m,
        string modifiers = "",
        int? providerIndex = 0,
        decimal? billed = 500m,
        long? npi = 1255962783,
        string providerKey = "844522008",
        string places = "22",
        string productKey = "essential-plan",
        int structureIndex = 1) =>
        new(paymentId, productKey, productKey, structureIndex, codeType, code, billingClass, places, modifiers, allowed, providerKey,
            providerIndex, billed, npi);
}
