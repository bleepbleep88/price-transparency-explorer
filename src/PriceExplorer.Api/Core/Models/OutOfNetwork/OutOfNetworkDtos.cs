using PriceExplorer.Api.Core.Models.Rates;

namespace PriceExplorer.Api.Core.Models.OutOfNetwork;

/// <summary>
/// What the plan allowed when it paid providers outside its network for a code, from the snapshot's allowed-amount
/// files. Plans list a provider and service only with 20 or more claims in a 90-day window, and each distinct amount
/// once with no claim count, so most codes have none and the figures describe listed amounts, not claims.
/// </summary>
/// <param name="Files">The snapshot's allowed-amount files, one per product, whatever their status.</param>
/// <param name="Groups">One per code, product, fee type and modifiers; empty when the files list nothing for the code.</param>
public sealed record OutOfNetworkResult(
    OutOfNetworkEcho Query,
    SnapshotRef Snapshot,
    IReadOnlyList<OutOfNetworkFile> Files,
    IReadOnlyList<OutOfNetworkGroup> Groups);

/// <param name="Code">The code as searched: "RC 450" becomes "450", with "RC" as <paramref name="CodeType"/>.</param>
/// <param name="MatchedCodes">Every stored form the code could mean ("450" → 450, 00450, 0450).</param>
public sealed record OutOfNetworkEcho(string Code, IReadOnlyList<string> MatchedCodes, string? CodeType);

/// <param name="Status">pending | loaded | partial | failed.</param>
public sealed record OutOfNetworkFile(int FileId, string ProductKey, string ProductLabel, string Url, string Status, DateOnly? FileLastUpdatedOn);

/// <summary>
/// One code's out-of-network payments in one product, fee type and set of modifiers. Facility and professional amounts
/// are separate bills, so they're never mixed. Places of service aren't split: one payment often lists several.
/// </summary>
/// <param name="PerUnitCode">In-network rates for this code are per unit (anesthesia, drugs, supplies); these amounts are for the whole service.</param>
/// <param name="Payments">Different amounts listed: unique payments (distinct allowed amounts per tax ID and place), after
/// duplicates collapse. Not a claim count; the files have none.</param>
/// <param name="Providers">Tax IDs paid.</param>
/// <param name="Billed">What the providers billed, one charge per provider on a payment; null when none was published.</param>
/// <param name="BilledToAllowed">
/// The median of billed ÷ allowed over each provider on a payment (4.81 = providers usually billed about 4.8 times what
/// the plan allowed), when 5 or more providers published a billed charge; null otherwise. It holds whatever an amount
/// covers, since the charge covers the same thing.
/// </param>
public sealed record OutOfNetworkGroup(
    string Key,
    string ProductKey,
    string ProductLabel,
    string CodeType,
    string Code,
    string FeeType,
    string FeeTypeLabel,
    IReadOnlyList<PlaceRef> Places,
    IReadOnlyList<ModifierRef> Modifiers,
    bool PerUnitCode,
    int Payments,
    int Providers,
    int Npis,
    AmountSummary Allowed,
    AmountSummary? Billed,
    decimal? BilledToAllowed);

/// <summary>A set of dollar amounts. From fewer than 5 providers they're listed instead of summarized, so no midpoint reads as a price.</summary>
/// <param name="Median">From 5 or more providers: linear interpolation, rounded to cents. Null with fewer.</param>
/// <param name="Listed">Every amount, lowest first, from fewer than 5 providers; empty otherwise.</param>
public sealed record AmountSummary(int Count, decimal Min, decimal? Median, decimal Max, IReadOnlyList<decimal> Listed);
