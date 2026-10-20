using PriceExplorer.Api.Core.Domain;

namespace PriceExplorer.Tests.Search;

public sealed class CodeSetRuleTests
{
    [Theory]
    [InlineData("Injection, propofol, 10 mg", 10, "mg", "10 mg")]
    [InlineData("Injection, onabotulinumtoxina, 1 unit", 1, "unit", "1 unit")]
    [InlineData("Low osmolar contrast material, 300-399 mg/ml iodine concentration, per ml", 1, "ml", "per ml")]
    [InlineData("Infusion, normal saline solution, 250 cc", 250, "ml", "250 cc")]
    [InlineData("Injection, leuprolide acetate (for depot suspension), per 3.75 mg", 3.75, "mg", "per 3.75 mg")]
    [InlineData("Injection, ceftriaxone sodium, per 250 mg", 250, "mg", "per 250 mg")]
    [InlineData("Injection, epoetin alfa, (for non-esrd use), 1000 units", 1000, "unit", "1000 units")]
    [InlineData("Injection, factor viii, per 1,000 i.u.", 1000, "unit", "per 1,000 i.u.")]
    [InlineData("Injection, morphine sulfate, up to 10 mg", 10, "mg", "up to 10 mg")]
    [InlineData("Injection, interferon beta-1a, 1 mcg for intramuscular use", 1, "mcg", "1 mcg")]
    [InlineData("INJ DEXMETH SODM PHOSHA 1MG", 1, "mg", "1MG")]
    [InlineData("Potassium chloride, per 2 meq", 2, "meq", "per 2 meq")]
    public void A_drug_names_its_billing_unit_last(string name, double amount, string unit, string text)
    {
        var parsed = DoseUnits.Parse(name);

        parsed.Should().NotBeNull();
        parsed!.Amount.Should().Be((decimal)amount);
        parsed.Unit.Should().Be(unit);
        parsed.Text.Should().Be(text);
    }

    [Theory]
    [InlineData("Unclassified drugs")]
    [InlineData("Injection, hyaluronan or derivative, for intra-articular injection, per dose")]
    [InlineData("Concentration 20 mg/ml")]
    [InlineData("")]
    [InlineData(null)]
    public void A_name_without_a_unit_amount_has_none(string? name) => DoseUnits.Parse(name).Should().BeNull();

    [Fact]
    public void Long_cms_names_are_cut_at_a_word()
    {
        var name = string.Join(' ', Enumerable.Repeat("management", 20));

        CodeNames.Short("Injection, propofol, 10 mg").Should().Be("Injection, propofol, 10 mg");
        CodeNames.Short(name).Should().EndWith("management…").And.HaveLength(99, "nine whole words and the ellipsis");
    }

    [Theory]
    [InlineData("99213", "Office visit, established patient", 3)]
    [InlineData("99202", "Office visit, new patient", 2)]
    [InlineData("99285", "Emergency room visit", 5)]
    public void Visit_codes_know_their_family_and_level(string code, string family, int level)
    {
        var found = ServiceCategories.FamilyOf("CPT", code);

        found!.Name.Should().Be(family);
        found.LevelOf(code).Should().Be(level);
        ServiceCategories.FamilyOf("HCPCS", code).Should().BeNull();
        ServiceCategories.FamilyOf("CPT", "45378").Should().BeNull();
    }
}
