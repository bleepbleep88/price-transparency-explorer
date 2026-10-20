namespace PriceExplorer.Tests.Support;

/// <summary>Small helpers for writing synthetic out-of-network allowed-amount files (schema 2.0 shape unless a test bends it).</summary>
internal static class AllowedAmountJson
{
    /// <summary>One provider on a payment: its billed charge and NPIs, each written as JSON (so a test can pass "\"N/A\"").</summary>
    public static string Provider(string billed = "481.00", string npi = "[1952476988]") =>
        $$"""{ "billed_charge": {{billed}}, "npi": {{npi}} }""";

    public static string Payment(string allowed = "43.24", string modifiers = "", params string[] providers) =>
        $$"""
        { "allowed_amount": {{allowed}}
          {{(modifiers.Length > 0 ? $", \"billing_code_modifier\": {modifiers}" : "")}},
          "providers": [ {{string.Join(", ", providers.Length == 0 ? [Provider()] : providers)}} ] }
        """;

    /// <summary>One tax ID's allowed amounts. <paramref name="tin"/> is the whole tin object as JSON.</summary>
    public static string Entry(string tin = Tin, string serviceCode = "[\"22\"]", string billingClass = "institutional", params string[] payments) =>
        $$"""
        { "tin": {{tin}}, "service_code": {{serviceCode}}, "billing_class": "{{billingClass}}",
          "payments": [ {{string.Join(", ", payments.Length == 0 ? [Payment()] : payments)}} ] }
        """;

    public const string Tin = """{ "type": "ein", "value": "131740114" }""";

    public static string Item(string codeType, string code, string name, params string[] entries) =>
        $$"""
        { "name": "{{name}}", "billing_code_type": "{{codeType}}", "billing_code_type_version": "2022",
          "billing_code": "{{code}}", "description": "{{name}}",
          "allowed_amounts": [ {{string.Join(", ", entries.Length == 0 ? [Entry()] : entries)}} ] }
        """;

    public static string File(IEnumerable<string> items, bool headerFirst = true)
    {
        const string header = """
            "reporting_entity_name": "Test Payer", "reporting_entity_type": "health insurance issuer",
            "version": "2.0.0", "last_updated_on": "2026-08-31"
            """;
        var outOfNetwork = $"\"out_of_network\": [ {string.Join(", ", items)} ]";
        return headerFirst ? $"{{ {header}, {outOfNetwork} }}" : $"{{ {outOfNetwork}, {header} }}";
    }
}
