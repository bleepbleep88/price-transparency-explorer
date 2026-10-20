using PriceExplorer.Api.Core.Handlers.Rates;
using PriceExplorer.Api.Core.Models.Rates;

namespace PriceExplorer.Tests.Search;

public sealed class ProviderFlagsTests
{
    [Fact]
    public void A_facility_rate_for_a_provider_whose_npis_are_all_individuals_is_flagged()
    {
        var flags = ProviderFlags.For(Group("facility", "CPT", "29881"), Profile(npis: 2, individuals: 2, "Family Medicine Physician"));

        flags.Should().ContainSingle(flag => flag.Kind == "facility_rate_individual_npis");
    }

    [Fact]
    public void A_facility_rate_with_a_facility_npi_is_not_flagged()
    {
        var profile = Profile(npis: 2, individuals: 1, "Ambulatory Surgical Clinic/Center");

        ProviderFlags.For(Group("facility", "CPT", "29881"), profile).Should().BeEmpty();
    }

    [Fact]
    public void An_anesthesia_rate_without_an_anesthesia_clinician_is_flagged()
    {
        ProviderFlags.For(Group("professional", "CPT", "00812"), Profile(npis: 1, individuals: 1, "Gastroenterology Physician"))
            .Should().ContainSingle(flag => flag.Kind == "anesthesia_without_anesthesia_clinician");

        ProviderFlags.For(Group("professional", "CPT", "00812"), Profile(npis: 1, individuals: 1, "Certified Registered Nurse Anesthetist"))
            .Should().BeEmpty();
    }

    [Fact]
    public void An_imaging_read_without_a_radiologist_is_flagged()
    {
        ProviderFlags.For(Group("professional", "CPT", "70553"), Profile(npis: 1, individuals: 1, "Family Nurse Practitioner"))
            .Should().ContainSingle(flag => flag.Kind == "imaging_without_radiologist");
    }

    [Fact]
    public void A_technical_component_imaging_rate_needs_no_radiologist()
    {
        var technical = Group("professional", "CPT", "71046") with { Modifiers = [new ModifierRef("TC", "Technical component")] };

        ProviderFlags.For(technical, Profile(npis: 2, individuals: 2, "Family Medicine Physician")).Should().BeEmpty(
            "whoever owns the X-ray machine bills the technical component");
    }

    [Theory]
    [InlineData(3, 1, "1 of the 3 NPIs listed for this rate is deactivated in the national registry.")]
    [InlineData(3, 2, "2 of the 3 NPIs listed for this rate are deactivated in the national registry.")]
    [InlineData(2, 2, "All 2 NPIs listed for this rate are deactivated in the national registry.")]
    [InlineData(1, 1, "The only NPI listed for this rate is deactivated in the national registry.")]
    public void Deactivated_npis_are_counted(int npis, int deactivated, string message)
    {
        var profile = Profile(npis, individuals: 0, "Ambulatory Surgical Clinic/Center") with { Deactivated = deactivated };

        ProviderFlags.For(Group("facility", "CPT", "45378"), profile).Should().ContainSingle().Which.Message.Should().Be(message);
    }

    [Fact]
    public void Deactivated_npis_never_make_the_other_flags()
    {
        // The registry keeps no specialty or type for a deactivated NPI, so nothing can be said about it.
        var allDeactivated = new ProviderProfile(1, 1, 0, 1, [], [], 0);

        ProviderFlags.For(Group("professional", "CPT", "00812"), allDeactivated).Select(flag => flag.Kind).Should().Equal("deactivated_npis");
    }

    [Fact]
    public void Without_directory_information_nothing_is_flagged()
    {
        ProviderFlags.For(Group("facility", "CPT", "29881"), new ProviderProfile(2, 0, 0, 0, [], [], 0)).Should().BeEmpty();
    }

    private static ProviderProfile Profile(int npis, int individuals, string specialty) =>
        new(npis, npis, individuals, 0, [new SpecialtyCount(specialty, null, npis)], [], 0);

    private static RateGroup Group(string feeType, string codeType, string code) =>
        new(
            Key: "k", ProductKey: "p", ProductLabel: "P", CodeType: codeType, Code: code, FeeType: feeType, FeeTypeLabel: feeType,
            SettingKey: "asc", SettingLabel: "Ambulatory surgery center", SettingNote: null, Places: [], Modifiers: [],
            NegotiatedType: "negotiated", Unit: "per procedure", PerUnit: false, BilledAs: [], ProviderCount: 1, RateCount: 1,
            StatsLevel: "list", Stats: new RateStats(1, null, null, null, null, null, 1, null, null), LowerQuartileGap: null,
            ListedRates: [1], Highlights: [], MatchedProviders: 0, Notes: []);
}
