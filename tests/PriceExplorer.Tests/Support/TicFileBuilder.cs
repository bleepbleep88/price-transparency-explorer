using System.Text;

namespace PriceExplorer.Tests.Support;

/// <summary>Small helpers for writing synthetic in-network files in tests (schema 2.0 shape unless a test bends it).</summary>
internal static class TicFileBuilder
{
    public const string Minkowitz = """
        { "provider_group_id": 1, "network_name": [], "provider_groups": [
          { "npi": [1396747861], "tin": { "type": "ein", "value": "11-3471596", "business_name": "Minkowitz Consulting Pathologists PC" } } ] }
        """;

    public const string CountyClinicWholeTin = """
        { "provider_group_id": 2, "network_name": [], "provider_groups": [
          { "npi": [0], "tin": { "type": "ein", "value": "07-1234567", "business_name": "County Health Clinic" } } ] }
        """;

    public static string Price(
        string rate = "1183.01",
        string serviceCode = "[\"24\"]",
        string billingClass = "institutional",
        string? additionalInformation = null,
        string modifiers = "",
        string expiration = "9999-12-31") =>
        $$"""
        { "negotiated_type": "negotiated", "negotiated_rate": {{rate}}, "expiration_date": "{{expiration}}",
          "service_code": {{serviceCode}}, "billing_class": "{{billingClass}}", "setting": "outpatient"
          {{(modifiers.Length > 0 ? $", \"billing_code_modifier\": {modifiers}" : "")}}
          {{(additionalInformation is null ? "" : $", \"additional_information\": \"{additionalInformation}\"")}} }
        """;

    public static string Item(string codeType, string code, string name, string references, params string[] prices) =>
        $$"""
        { "negotiation_arrangement": "ffs", "name": "{{name}}", "billing_code_type": "{{codeType}}",
          "billing_code_type_version": "2022", "billing_code": "{{code}}", "description": "{{name}}",
          "negotiated_rates": [ { "provider_references": {{references}}, "negotiated_prices": [ {{string.Join(", ", prices)}} ] } ] }
        """;

    public static string File(IEnumerable<string> providerReferences, IEnumerable<string> items, bool referencesFirst = true)
    {
        var references = $"\"provider_references\": [ {string.Join(", ", providerReferences)} ]";
        var inNetwork = $"\"in_network\": [ {string.Join(", ", items)} ]";
        var body = referencesFirst ? $"{references}, {inNetwork}" : $"{inNetwork}, {references}";
        return $$"""
            { "reporting_entity_name": "Test Payer", "reporting_entity_type": "health insurance issuer",
              "version": "2.0.0", "last_updated_on": "2026-09-02", {{body}} }
            """;
    }

    public static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);
}
