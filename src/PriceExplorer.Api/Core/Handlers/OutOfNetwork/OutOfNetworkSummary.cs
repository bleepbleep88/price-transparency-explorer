using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Handlers.Rates;
using PriceExplorer.Api.Core.Models.OutOfNetwork;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Api.Core.Handlers.OutOfNetwork;

/// <summary>
/// Groups out-of-network payments by code, product, fee type and modifiers, and summarizes each group's allowed amounts
/// (one per unique payment) and billed charges (one per provider on a payment). Pure; covered by unit tests.
/// </summary>
internal static class OutOfNetworkSummary
{
    public static IReadOnlyList<OutOfNetworkGroup> Summarize(IReadOnlyList<OutOfNetworkFact> facts, ReferenceCatalog catalog) =>
        facts
            .GroupBy(fact => new GroupKey(
                fact.ProductKey, fact.ProductLabel, fact.StructureIndex, fact.BillingCodeType, fact.BillingCode,
                RateSearchAssembler.FeeTypeOf(fact.BillingClass), fact.Modifiers))
            .OrderBy(group => group.Key.CodeType, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Code, StringComparer.Ordinal)
            .ThenBy(group => group.Key.StructureIndex)
            .ThenBy(group => group.Key.FeeType, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Modifiers, StringComparer.Ordinal)
            .Select(group => ToGroup(group.Key, [.. group], catalog))
            .ToList();

    /// <summary>Lists the amounts under 5 (no midpoint reads as a price, as with rates); min, median and max from 5 on.</summary>
    public static AmountSummary Amounts(IEnumerable<decimal> amounts)
    {
        var sorted = amounts.Order().ToList();
        if (sorted.Count == 0)
        {
            throw new ArgumentException("No amounts to summarize.", nameof(amounts));
        }

        var listed = sorted.Count < RateStatistics.MinimumForBasicStats;
        return new AmountSummary(sorted.Count, sorted[0], listed ? null : RateStatistics.Median(sorted), sorted[^1], listed ? sorted : []);
    }

    public static string FeeTypeLabel(string feeType) => feeType switch
    {
        "facility" => "Facility fee",
        "professional" => "Professional (clinician) fee",
        _ => "Fee",
    };

    private static OutOfNetworkGroup ToGroup(GroupKey key, List<OutOfNetworkFact> facts, ReferenceCatalog catalog)
    {
        var payments = facts.DistinctBy(fact => fact.PaymentId).ToList();
        var billed = facts
            .Where(fact => fact is { ProviderIndex: not null, BilledCharge: not null })
            .DistinctBy(fact => (fact.PaymentId, fact.ProviderIndex))
            .Select(fact => fact.BilledCharge!.Value)
            .ToList();

        return new OutOfNetworkGroup(
            Key: string.Join('|', key.ProductKey, key.CodeType, key.Code, key.FeeType, key.Modifiers),
            key.ProductKey,
            key.ProductLabel,
            key.CodeType,
            key.Code,
            key.FeeType,
            FeeTypeLabel(key.FeeType),
            Places: payments
                .SelectMany(payment => payment.ServiceCodes.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(pos => new PlaceRef(pos, catalog.PlaceName(pos)))
                .ToList(),
            Modifiers: key.Modifiers.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(code => new ModifierRef(code, catalog.Modifier(code).Label))
                .ToList(),
            PerUnitCode: ServiceCategories.Classify(key.CodeType, key.Code).PerUnit,
            Payments: payments.Count,
            Providers: payments.Select(payment => payment.ProviderKey).Distinct(StringComparer.Ordinal).Count(),
            Npis: facts.Where(fact => fact.Npi is not null).Select(fact => fact.Npi).Distinct().Count(),
            Allowed: Amounts(payments.Select(payment => payment.AllowedAmount)),
            Billed: billed.Count == 0 ? null : Amounts(billed));
    }

    private sealed record GroupKey(
        string ProductKey, string ProductLabel, int StructureIndex, string CodeType, string Code, string FeeType, string Modifiers);
}

/// <summary>One row of OutOfNetworkPayments.sql: a payment with one of its providers' NPIs (nulls when none are listed).</summary>
internal sealed record OutOfNetworkFact(
    long PaymentId,
    string ProductKey,
    string ProductLabel,
    int StructureIndex,
    string BillingCodeType,
    string BillingCode,
    string BillingClass,
    string ServiceCodes,
    string Modifiers,
    decimal AllowedAmount,
    string ProviderKey,
    int? ProviderIndex,
    decimal? BilledCharge,
    long? Npi);
