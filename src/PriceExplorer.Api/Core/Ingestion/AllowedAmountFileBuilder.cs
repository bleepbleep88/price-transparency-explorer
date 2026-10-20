using System.Globalization;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Ingestion.Tic;

namespace PriceExplorer.Api.Core.Ingestion;

/// <summary>
/// Collects one out-of-network allowed-amount file into unique payments while the parser streams it. Same rules as
/// <see cref="InNetworkFileBuilder"/>:
/// <list type="bullet">
/// <item>Codes get their leading zeros back (RC 450 is 0450, CPT 142 is 00142); the published code is kept.</item>
/// <item>Identical payments collapse into one with a copy count. Revenue-code items repeat the same payments under
/// unrelated item names (RC 0450: 120 published, 12 unique), so item names are kept only as source labels.</item>
/// <item>EINs are 9 digits. One written as a number loses its leading zero (06-0646659 as 60646659), so an 8-digit
/// value gets it back; never-issued prefixes are flagged so they can be masked.</item>
/// <item>Bad values never stop the file: each is counted under a reject reason.</item>
/// </list>
/// </summary>
internal sealed class AllowedAmountFileBuilder
{
    private const int MaxLabelsKept = 3;

    private readonly Dictionary<PaymentKey, PaymentEntry> _payments = new();
    private readonly List<OutOfNetworkItemRecord> _items = [];
    private readonly HashSet<long> _distinctNpis = [];
    private readonly HashSet<long> _invalidNpis = [];
    private readonly HashSet<string> _distinctEins = new(StringComparer.Ordinal);
    private readonly HashSet<string> _restoredEins = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RejectTally> _rejects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);

    private int _itemIndex = -1;
    private int _itemsParsed;
    private long _paymentsRaw;
    private int _taxIdEntries;
    private int _unverifiedTaxIdEntries;

    public string? ReportingEntityName { get; private set; }
    public string? ReportingEntityType { get; private set; }
    public string? SchemaVersion { get; private set; }
    public DateOnly? LastUpdatedOn { get; private set; }

    public IReadOnlyList<OutOfNetworkItemRecord> Items => _items;
    public IReadOnlyCollection<PaymentEntry> Payments => _payments.Values;
    public IReadOnlyCollection<RejectTally> Rejects => _rejects.Values;

    /// <summary>Items parsed so far; used for "stopped after item N" messages.</summary>
    public int ItemsParsed => _itemsParsed;

    public AllowedAmountStats GetStats() => new(
        Items: _itemsParsed,
        PaymentsRaw: _paymentsRaw,
        PaymentsUnique: _payments.Count,
        TaxIdEntries: _taxIdEntries,
        DistinctNpis: _distinctNpis.Count,
        DistinctEins: _distinctEins.Count,
        InvalidNpis: _invalidNpis.Count,
        UnverifiedTaxIdEntries: _unverifiedTaxIdEntries,
        EinsWithLeadingZeroRestored: _restoredEins.Count,
        RejectedPayments: _rejects.Values.Where(r => r.AffectsPrices).Sum(r => r.Count));

    public void AddItem(OutOfNetworkItem? item)
    {
        var itemIndex = ++_itemIndex;
        _itemsParsed++;

        if (item is null)
        {
            Reject(RejectReasons.NullElement, $"out_of_network[{itemIndex}]");
            return;
        }

        var entries = item.AllowedAmounts;
        var published = entries?.Sum(entry => entry?.Payments?.Count ?? 0) ?? 0;
        var billingType = BillingCodes.NormalizeType(item.BillingCodeType);
        var billingRaw = (item.BillingCode ?? "").Trim();
        if (billingType.Length == 0 || billingRaw.Length == 0)
        {
            _paymentsRaw += published;
            Reject("item_missing_billing_code", $"out_of_network[{itemIndex}]", published, affectsPayments: true);
            return;
        }

        var code = new ItemCode(Intern(billingType), Intern(BillingCodes.Canonicalize(billingType, billingRaw)), Intern(billingRaw));
        var label = Intern(Clean(item.Name) ?? "");
        _items.Add(new OutOfNetworkItemRecord(itemIndex, code.Type, code.Code, code.Raw, Clean(item.Name), Clean(item.Description), published));

        if (entries is null || entries.Count == 0)
        {
            Reject("item_without_allowed_amounts", $"{code.Type} {code.Raw}");
            return;
        }

        for (var allowedIndex = 0; allowedIndex < entries.Count; allowedIndex++)
        {
            var path = $"out_of_network[{itemIndex}].allowed_amounts[{allowedIndex}]";
            if (entries[allowedIndex] is not { } entry)
            {
                Reject(RejectReasons.NullElement, path);
                continue;
            }

            var payments = entry.Payments ?? [];
            if (ReadTaxId(entry.Tin, path, payments.Count) is not { } taxId)
            {
                _paymentsRaw += payments.Count;
                continue;
            }

            if (payments.Count == 0)
            {
                Reject("allowed_amount_without_payments", path);
                continue;
            }

            var places = Intern(NormalizePlaces(entry.ServiceCode));
            var billingClass = Intern(Lower(entry.BillingClass) ?? "unspecified");
            for (var paymentIndex = 0; paymentIndex < payments.Count; paymentIndex++)
            {
                _paymentsRaw++;
                var at = new PaymentPath(itemIndex, allowedIndex, paymentIndex);
                AddPayment(payments[paymentIndex], code, label, taxId, places, billingClass, at);
            }
        }
    }

    public void Complete(AllowedAmountFileRoot? root)
    {
        if (root is null)
        {
            return;
        }

        ReportingEntityName = Clean(root.ReportingEntityName);
        ReportingEntityType = Clean(root.ReportingEntityType);
        SchemaVersion = Clean(root.Version);
        LastUpdatedOn = InNetworkFileBuilder.ParseDate(root.LastUpdatedOn);
    }

    private void AddPayment(
        OutOfNetworkPayment? payment, ItemCode code, string label, TaxIdentity taxId, string places, string billingClass, PaymentPath at)
    {
        var path = at.ToString();
        if (payment is null)
        {
            Reject("null_payment", path, affectsPayments: true);
            return;
        }

        if (payment.AllowedAmount is not { } allowed)
        {
            Reject("allowed_amount_not_a_number", path, affectsPayments: true);
            return;
        }

        if (allowed <= 0)
        {
            Reject("allowed_amount_not_positive", path, affectsPayments: true);
            return;
        }

        var providers = ReadProviders(payment.Providers, path);
        var key = new PaymentKey(
            code.Type, code.Code, taxId.Identity, places, billingClass, decimal.Round(allowed, 4),
            Intern(InNetworkFileBuilder.NormalizeModifiers(payment.BillingCodeModifier)),
            Intern(string.Join(';', providers.Select(provider => provider.Key).Order(StringComparer.Ordinal))));

        if (_payments.TryGetValue(key, out var existing))
        {
            existing.Occurrences++;
            existing.AddLabel(label, MaxLabelsKept);
            return;
        }

        var entry = new PaymentEntry(key, code.Raw, taxId, providers, at);
        entry.AddLabel(label, MaxLabelsKept);
        _payments[key] = entry;
    }

    private TaxIdentity? ReadTaxId(TaxpayerId? tin, string path, int payments)
    {
        var type = Intern(Lower(tin?.Type) ?? "");
        var value = Clean(tin?.Value);
        if (type.Length == 0 || value is null)
        {
            Reject("allowed_amount_without_tax_id", path, payments, affectsPayments: true);
            return null;
        }

        string? ein = null;
        if (type == "ein")
        {
            ein = Ein.Normalize(value);

            // A tax ID written as a number loses its leading zero (06-0646659 becomes 60646659); 8 bare digits get it back.
            if (ein is null && value.Length == 8 && value.All(char.IsAsciiDigit))
            {
                ein = "0" + value;
                _restoredEins.Add(ein);
            }

            if (ein is null)
            {
                Reject("ein_not_nine_digits", value);
            }
            else
            {
                ein = Intern(ein);
                _distinctEins.Add(ein);
            }
        }

        var unverified = ein is not null && Ein.HasNeverIssuedPrefix(ein);
        _taxIdEntries++;
        if (unverified)
        {
            _unverifiedTaxIdEntries++;
        }

        return new TaxIdentity(type, Intern(value), ein, unverified);
    }

    private List<OutOfNetworkProviderRecord> ReadProviders(List<OutOfNetworkProvider?>? providers, string path)
    {
        var result = new List<OutOfNetworkProviderRecord>(providers?.Count ?? 0);
        if (providers is null || providers.Count == 0)
        {
            // The payment still counts: its tax ID says who was paid. Only the billed charge and NPIs are missing.
            Reject("payment_without_providers", path);
            return result;
        }

        for (var providerIndex = 0; providerIndex < providers.Count; providerIndex++)
        {
            var at = $"{path}.providers[{providerIndex}]";
            if (providers[providerIndex] is not { } provider)
            {
                Reject(RejectReasons.NullElement, at);
                continue;
            }

            var billed = provider.BilledCharge;
            if (billed is null)
            {
                Reject("billed_charge_not_a_number", at);
            }
            else if (billed <= 0)
            {
                Reject("billed_charge_not_positive", at);
                billed = null;
            }

            var npis = new List<OutOfNetworkNpi>(provider.Npi?.Count ?? 0);
            foreach (var value in provider.Npi ?? [])
            {
                if (value is not { } npi)
                {
                    Reject("npi_not_a_number", null);
                    continue;
                }

                // As in rate files, 0 stands for "no individual NPI listed".
                if (npi == 0)
                {
                    continue;
                }

                var valid = Npi.IsValid(npi);
                if (!valid)
                {
                    _invalidNpis.Add(npi);
                }

                _distinctNpis.Add(npi);
                npis.Add(new OutOfNetworkNpi(npi, valid));
            }

            var rounded = billed is { } charge ? decimal.Round(charge, 4) : (decimal?)null;
            result.Add(new OutOfNetworkProviderRecord(providerIndex, rounded, npis));
        }

        return result;
    }

    private static string NormalizePlaces(List<string?>? serviceCodes)
    {
        var places = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var raw in serviceCodes ?? [])
        {
            var place = InNetworkFileBuilder.NormalizePlace(raw);
            if (place.Length > 0)
            {
                places.Add(place);
            }
        }

        return string.Join(',', places);
    }

    private void Reject(string reason, string? example, long count = 1, bool affectsPayments = false)
    {
        if (!_rejects.TryGetValue(reason, out var tally))
        {
            tally = new RejectTally(reason, affectsPayments);
            _rejects[reason] = tally;
        }

        tally.Count += count;
        tally.Example ??= example;
    }

    private string Intern(string value)
    {
        if (_strings.TryGetValue(value, out var existing))
        {
            return existing;
        }

        _strings[value] = value;
        return value;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Lower(string? value) => Clean(value)?.ToLowerInvariant();

    private readonly record struct ItemCode(string Type, string Code, string Raw);
}

/// <summary>Where a payment's first published copy sits: out_of_network[i].allowed_amounts[j].payments[k].</summary>
internal readonly record struct PaymentPath(int ItemIndex, int AllowedIndex, int PaymentIndex)
{
    public override string ToString() =>
        $"out_of_network[{ItemIndex}].allowed_amounts[{AllowedIndex}].payments[{PaymentIndex}]";
}

/// <summary>Who was paid, as published. <paramref name="Ein"/> is the 9 digits (leading zero restored), or null.</summary>
internal sealed record TaxIdentity(string Type, string Value, string? Ein, bool EinUnverified)
{
    /// <summary>The same tax ID however it was spelled ("060646659", "60646659", "06-0646659").</summary>
    public string Identity => $"{Type}:{Ein ?? Value}";
}

/// <summary>Everything that makes two published payments the same fact.</summary>
internal readonly record struct PaymentKey(
    string BillingCodeType,
    string BillingCode,
    string TaxId,
    string Places,
    string BillingClass,
    decimal AllowedAmount,
    string Modifiers,
    string Providers);

internal sealed class PaymentEntry(
    PaymentKey key, string billingCodeRaw, TaxIdentity taxId, IReadOnlyList<OutOfNetworkProviderRecord> providers, PaymentPath path)
{
    private HashSet<string>? _labels;

    public PaymentKey Key { get; } = key;
    public string BillingCodeRaw { get; } = billingCodeRaw;
    public TaxIdentity TaxId { get; } = taxId;
    public IReadOnlyList<OutOfNetworkProviderRecord> Providers { get; } = providers;
    public PaymentPath Path { get; } = path;

    /// <summary>How many published copies collapsed into this one.</summary>
    public int Occurrences { get; set; } = 1;

    public List<string> LabelSamples { get; } = [];

    public int LabelCount => _labels?.Count ?? 0;

    public void AddLabel(string label, int maxSamples)
    {
        if (label.Length == 0)
        {
            return;
        }

        _labels ??= new HashSet<string>(StringComparer.Ordinal);
        if (_labels.Add(label) && LabelSamples.Count < maxSamples)
        {
            LabelSamples.Add(label);
        }
    }
}

/// <param name="ProviderIndex">Position in the payment's providers[] as published.</param>
/// <param name="BilledCharge">Null when it wasn't a positive number.</param>
internal sealed record OutOfNetworkProviderRecord(int ProviderIndex, decimal? BilledCharge, IReadOnlyList<OutOfNetworkNpi> Npis)
{
    /// <summary>The billed charge and NPIs, the part of a provider entry that makes two payments different.</summary>
    public string Key => $"{BilledCharge?.ToString(CultureInfo.InvariantCulture) ?? "-"}|{string.Join(',', Npis.Select(n => n.Npi).Order())}";
}

internal sealed record OutOfNetworkNpi(long Npi, bool Valid);

/// <param name="Payments">Payments published under this item, before duplicates collapse.</param>
internal sealed record OutOfNetworkItemRecord(
    int ItemIndex, string BillingCodeType, string BillingCode, string BillingCodeRaw, string? Name, string? Description, int Payments);

internal sealed record AllowedAmountStats(
    int Items,
    long PaymentsRaw,
    int PaymentsUnique,
    int TaxIdEntries,
    int DistinctNpis,
    int DistinctEins,
    int InvalidNpis,
    int UnverifiedTaxIdEntries,
    int EinsWithLeadingZeroRestored,
    long RejectedPayments)
{
    public long DuplicatesRemoved => PaymentsRaw - RejectedPayments - PaymentsUnique;
}
