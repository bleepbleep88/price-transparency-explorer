using PriceExplorer.Api.Core.Domain;

namespace PriceExplorer.Tests.Search;

public sealed class CodeSetRuleTests
{
    [Theory]
    [InlineData("Injection, propofol, 10 mg", 10, "mg", "10 mg")]
    [InlineData("Injection, onabotulinumtoxina, 1 unit", 1, "unit", "1 unit")]
    [InlineData("Low osmolar contrast material, 300-399 mg/ml iodine concentration, per ml", 1, "ml", "1 ml")]
    [InlineData("Infusion, normal saline solution, 250 cc", 250, "ml", "250 ml")]
    [InlineData("Injection, leuprolide acetate (for depot suspension), per 3.75 mg", 3.75, "mg", "3.75 mg")]
    [InlineData("Injection, ceftriaxone sodium, per 250 mg", 250, "mg", "250 mg")]
    [InlineData("Injection, epoetin alfa, (for non-esrd use), 1000 units", 1000, "unit", "1,000 units")]
    [InlineData("Injection, factor viii, per 1,000 i.u.", 1000, "unit", "1,000 units")]
    [InlineData("Injection, morphine sulfate, up to 10 mg", 10, "mg", "10 mg")]
    [InlineData("Injection, interferon beta-1a, 1 mcg for intramuscular use", 1, "mcg", "1 mcg")]
    [InlineData("INJ DEXMETH SODM PHOSHA 1MG", 1, "mg", "1 mg")]
    [InlineData("Potassium chloride, per 2 meq", 2, "meq", "2 meq")]
    // A total restated alone in parentheses is the unit.
    [InlineData("Injection, piperacillin sodium/tazobactam sodium, 1 gram/0.125 grams (1.125 grams)", 1.125, "g", "1.125 g")]
    [InlineData("Injection, aztreonam/avibactam, 7.5 mg/2.5 mg (10 mg)", 10, "mg", "10 mg")]
    [InlineData("Injection, rho d immune globulin, human, full dose, 300 micrograms (1500 i.u.)", 1500, "unit", "1,500 units")]
    // "(500 ml = 1 unit)" means one unit is 500 ml.
    [InlineData("Infusion, normal saline solution, sterile (500 ml = 1 unit)", 500, "ml", "500 ml")]
    [InlineData("5% dextrose/water (500 ml = 1 unit)", 500, "ml", "500 ml")]
    public void A_drug_name_states_its_billing_unit(string name, double amount, string unit, string text)
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
    // Two or more amounts name no single unit, so there's no calculator.
    [InlineData("Injection, betamethasone acetate 3 mg and betamethasone sodium phosphate 3 mg")]
    [InlineData("Injection, acetaminophen 10 mg and ibuprofen 3 mg")]
    [InlineData("Injection, ceftolozane 50 mg and tazobactam 25 mg")]
    [InlineData("Injection, thyrotropin alpha, 0.9 mg, provided in 1.1 mg vial")]
    [InlineData("Injection, liposomal, 1 mg daunorubicin and 2.27 mg cytarabine")]
    [InlineData("Injection, neostigmine methylsulfate 0.1 mg and glycopyrrolate 0.02 mg")]
    [InlineData("Netupitant 300 mg and palonosetron 0.5 mg, oral")]
    [InlineData("Injection, fosnetupitant 235 mg and palonosetron 0.25 mg")]
    [InlineData("Injection, sulfamethoxazole 5 mg and trimethoprim 1 mg")]
    [InlineData("Injection, imipenem 4 mg, cilastatin 4 mg and relebactam 2 mg")]
    [InlineData("Injection, testosterone cypionate, 1 cc, 200 mg")]
    [InlineData("Cellesta flowable amnion (25 mg per cc); per 0.5 cc")]
    public void A_name_without_one_unit_amount_has_none(string? name) => DoseUnits.Parse(name).Should().BeNull();

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
