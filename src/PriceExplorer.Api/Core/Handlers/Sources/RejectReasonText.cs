using System.Collections.Frozen;

namespace PriceExplorer.Api.Core.Handlers.Sources;

/// <summary>Plain words for each reject reason the parsers record, and how examples are shown.</summary>
internal static class RejectReasonText
{
    // What the problem is, then what loading did about it. Keys are the reasons InNetworkFileBuilder and
    // AllowedAmountFileBuilder record.
    private static readonly FrozenDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        ["null_element"] = "An empty (null) entry in a list. Skipped.",
        ["null_price"] = "An empty (null) price. Skipped.",
        ["rate_not_a_number"] = "A rate that isn't a number. The price is skipped.",
        ["rate_not_positive"] = "A rate of zero or less. The price is skipped.",
        ["item_missing_billing_code"] = "An item without a billing code. Everything under it is skipped.",
        ["item_without_negotiated_rates"] = "An item with no rates. Nothing to load.",
        ["rate_without_providers"] = "A rate that names no providers. Its prices are skipped.",
        ["provider_reference_without_id"] = "A provider reference without an ID. Skipped.",
        ["remote_provider_reference_not_fetched"] = "A provider reference that points to another file. That file isn't fetched.",
        ["duplicate_provider_group_id"] = "A provider group ID defined twice. The first one is kept.",
        ["provider_reference_not_a_number"] = "A rate's provider reference that isn't a number. Skipped.",
        ["provider_reference_without_groups"] = "A provider reference with no provider groups. Skipped.",
        ["ein_not_nine_digits"] = "A tax ID marked as an EIN that isn't 9 digits. Kept as published, not read as an EIN.",
        ["npi_not_a_number"] = "An NPI that isn't a number. Skipped.",
        ["null_payment"] = "An empty (null) payment. Skipped.",
        ["allowed_amount_not_a_number"] = "An allowed amount that isn't a number. The payment is skipped.",
        ["allowed_amount_not_positive"] = "An allowed amount of zero or less. The payment is skipped.",
        ["allowed_amount_without_tax_id"] = "Allowed amounts with no tax ID, so no provider to give them to. Skipped.",
        ["allowed_amount_without_payments"] = "A tax ID listed with no payments. Nothing to load.",
        ["item_without_allowed_amounts"] = "An item with no allowed amounts. Nothing to load.",
        ["payment_without_providers"] = "A payment that lists no providers. Kept, without billed charges or NPIs.",
        ["billed_charge_not_a_number"] = "A billed charge that isn't a number. The payment is kept without it.",
        ["billed_charge_not_positive"] = "A billed charge of zero or less. The payment is kept without it.",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Reasons whose example is a tax ID value as published, which could be a Social Security number.</summary>
    private static readonly FrozenSet<string> TaxIdExamples = new[] { "ein_not_nine_digits" }.ToFrozenSet(StringComparer.Ordinal);

    public static string Describe(string reason) =>
        Descriptions.TryGetValue(reason, out var description)
            ? description
            : $"{char.ToUpperInvariant(reason[0])}{reason[1..].Replace('_', ' ')}.";

    /// <summary>The example as shown: tax ID values keep only their last 4 digits ("12-3456789" → "••-•••6789").</summary>
    public static string? Example(string reason, string? example) =>
        example is not null && TaxIdExamples.Contains(reason) ? MaskDigits(example) : example;

    /// <summary>Every digit but the last 4 becomes "•"; other characters stay, so the shape of what was published shows.</summary>
    internal static string MaskDigits(string value)
    {
        var digitsLeft = value.Count(char.IsAsciiDigit);
        return string.Concat(value.Select(c => char.IsAsciiDigit(c) && digitsLeft-- > 4 ? '•' : c));
    }
}
