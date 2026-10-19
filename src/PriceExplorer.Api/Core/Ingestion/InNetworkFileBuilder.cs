using System.Globalization;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Ingestion.Tic;

namespace PriceExplorer.Api.Core.Ingestion;

/// <summary>
/// Collects one in-network file into a clean, de-duplicated shape while the parser streams it.
/// <para>
/// Rules (see README "Normalization"):
/// <list type="bullet">
/// <item>Revenue-code rows carry the real procedure code in additional_information; it becomes the searchable code.</item>
/// <item>Identical prices (same code, setting, modifiers, rate, providers...) collapse into one rate with an occurrence count.</item>
/// <item>Revenue-code item names don't describe the codes inside them, so they're kept only as source labels.</item>
/// <item>Bad values never stop the file: each one is counted under a reject reason.</item>
/// </list>
/// </para>
/// </summary>
internal sealed class InNetworkFileBuilder
{
    private const int MaxLabelsKept = 3;

    private readonly Dictionary<RateKey, RateEntry> _rates = new();
    private readonly List<ProviderGroupRecord> _groups = [];
    private readonly List<GroupNpiRecord> _groupNpis = [];
    private readonly Dictionary<(string Type, string Code), CodeDescriptionRecord> _descriptions = new();
    private readonly HashSet<int> _definedReferences = [];
    private readonly HashSet<int> _usedReferences = [];
    private readonly HashSet<long> _distinctNpis = [];
    private readonly HashSet<long> _invalidNpis = [];
    private readonly HashSet<string> _distinctEins = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RejectTally> _rejects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);

    private int _itemIndex = -1;
    private int _nextInlineReferenceId = -1;
    private int _items;
    private long _pricesRaw;
    private int _providerReferences;
    private int _einLevelGroups;
    private int _unverifiedEinGroups;

    public string? ReportingEntityName { get; private set; }
    public string? ReportingEntityType { get; private set; }
    public string? SchemaVersion { get; private set; }
    public DateOnly? LastUpdatedOn { get; private set; }

    public IReadOnlyCollection<RateEntry> Rates => _rates.Values;
    public IReadOnlyList<ProviderGroupRecord> Groups => _groups;
    public IReadOnlyList<GroupNpiRecord> GroupNpis => _groupNpis;
    public IReadOnlyCollection<CodeDescriptionRecord> Descriptions => _descriptions.Values;
    public IReadOnlyCollection<RejectTally> Rejects => _rejects.Values;

    /// <summary>Items parsed so far; used for progress and for "stopped after item N" messages.</summary>
    public int ItemsParsed => _items;

    public FileStats GetStats() => new(
        Items: _items,
        PricesRaw: _pricesRaw,
        RatesUnique: _rates.Count,
        ProviderReferences: _providerReferences,
        ProviderGroups: _groups.Count,
        DistinctNpis: _distinctNpis.Count,
        DistinctEins: _distinctEins.Count,
        InvalidNpis: _invalidNpis.Count,
        EinLevelGroups: _einLevelGroups,
        UnverifiedEinGroups: _unverifiedEinGroups,
        DanglingReferences: _usedReferences.Count(id => !_definedReferences.Contains(id)),
        RejectedPrices: _rejects.Values.Where(r => r.AffectsPrices).Sum(r => r.Count));

    public void AddProviderReference(ProviderReference reference)
    {
        _providerReferences++;

        if (reference.ProviderGroupId is not { } referenceId)
        {
            Reject("provider_reference_without_id", null);
            return;
        }

        if (!string.IsNullOrWhiteSpace(reference.Location))
        {
            Reject("remote_provider_reference_not_fetched", reference.Location);
        }

        if (!_definedReferences.Add(referenceId))
        {
            Reject("duplicate_provider_group_id", referenceId.ToString(CultureInfo.InvariantCulture));
            return;
        }

        var networks = reference.NetworkName is { Count: > 0 }
            ? string.Join(" | ", reference.NetworkName.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()))
            : null;

        AddGroups(referenceId, reference.ProviderGroups, string.IsNullOrEmpty(networks) ? null : networks);
    }

    public void AddItem(InNetworkItem item)
    {
        var itemIndex = ++_itemIndex;
        _items++;

        var billingType = BillingCodes.NormalizeType(item.BillingCodeType);
        var billingRaw = (item.BillingCode ?? "").Trim();
        var priceCount = item.NegotiatedRates?.Sum(block => block.NegotiatedPrices?.Count ?? 0) ?? 0;
        if (billingType.Length == 0 || billingRaw.Length == 0)
        {
            _pricesRaw += priceCount;
            Reject("item_missing_billing_code", $"in_network[{itemIndex}]", priceCount, affectsPrices: true);
            return;
        }

        var billingCode = Intern(BillingCodes.Canonicalize(billingType, billingRaw));
        billingRaw = Intern(billingRaw);
        billingType = Intern(billingType);
        var label = Intern(Clean(item.Name) ?? "");
        var arrangement = Intern(Lower(item.NegotiationArrangement) ?? "ffs");

        if (billingType is BillingCodes.Cpt or BillingCodes.Hcpcs)
        {
            RememberDescription(billingType, billingCode, item);
        }

        var blocks = item.NegotiatedRates;
        if (blocks is null || blocks.Count == 0)
        {
            Reject("item_without_negotiated_rates", $"{billingType} {billingRaw}");
            return;
        }

        for (var rateIndex = 0; rateIndex < blocks.Count; rateIndex++)
        {
            var block = blocks[rateIndex];
            var prices = block.NegotiatedPrices ?? [];
            var references = ResolveReferences(block);
            if (references.Length == 0)
            {
                _pricesRaw += prices.Count;
                Reject("rate_without_providers", $"in_network[{itemIndex}].negotiated_rates[{rateIndex}]", prices.Count, affectsPrices: true);
                continue;
            }

            var referencesKey = Intern(string.Join(',', references));

            for (var priceIndex = 0; priceIndex < prices.Count; priceIndex++)
            {
                _pricesRaw++;
                AddPrice(prices[priceIndex], itemIndex, rateIndex, priceIndex, billingType, billingCode, billingRaw, arrangement, label, references, referencesKey);
            }
        }
    }

    public void Complete(InNetworkFileRoot? root)
    {
        if (root is null)
        {
            return;
        }

        ReportingEntityName = Clean(root.ReportingEntityName);
        ReportingEntityType = Clean(root.ReportingEntityType);
        SchemaVersion = Clean(root.Version);
        LastUpdatedOn = ParseDate(root.LastUpdatedOn);
    }

    private void AddPrice(
        NegotiatedPrice price, int itemIndex, int rateIndex, int priceIndex,
        string billingType, string billingCode, string billingRaw, string arrangement, string label,
        int[] references, string referencesKey)
    {
        var path = $"in_network[{itemIndex}].negotiated_rates[{rateIndex}].negotiated_prices[{priceIndex}]";

        if (price.NegotiatedRate is not { } rate)
        {
            Reject("rate_not_a_number", path, affectsPrices: true);
            return;
        }

        if (rate <= 0)
        {
            Reject("rate_not_positive", path, affectsPrices: true);
            return;
        }

        var negotiatedType = Intern(Lower(price.NegotiatedType) ?? "unspecified");
        var billingClass = Intern(Lower(price.BillingClass) ?? "unspecified");
        var setting = Intern(Lower(price.Setting) ?? "");
        var places = NormalizePlaces(price.ServiceCode);
        var modifiers = Intern(NormalizeModifiers(price.BillingCodeModifier));
        var expiration = ParseExpiration(price.ExpirationDate);
        var additionalInformation = Clean(price.AdditionalInformation);

        string codeType, code, codeSource;
        if (billingType == BillingCodes.RevenueCode && BillingCodes.TryParseProcedureCode(additionalInformation, out var parsedType, out var parsedCode))
        {
            codeType = parsedType;
            code = Intern(parsedCode);
            codeSource = CodeSources.AdditionalInformation;
        }
        else
        {
            codeType = billingType;
            code = billingCode;
            codeSource = CodeSources.BillingCode;
        }

        var key = new RateKey(
            billingType, billingCode, codeType, code, arrangement, negotiatedType, billingClass, setting,
            decimal.Round(rate, 4), expiration, Intern(string.Join(',', places)), modifiers,
            Intern(additionalInformation ?? ""), referencesKey);

        if (_rates.TryGetValue(key, out var existing))
        {
            existing.Occurrences++;
            existing.AddLabel(label, MaxLabelsKept);
            return;
        }

        var entry = new RateEntry(key, billingRaw, codeSource, places, references, itemIndex, rateIndex, priceIndex);
        entry.AddLabel(label, MaxLabelsKept);
        _rates[key] = entry;
    }

    private int[] ResolveReferences(NegotiatedRateBlock block)
    {
        var references = new List<int>(block.ProviderReferences?.Count ?? 1);
        foreach (var reference in block.ProviderReferences ?? [])
        {
            if (reference is { } id)
            {
                references.Add(id);
                _usedReferences.Add(id);
            }
            else
            {
                Reject("provider_reference_not_a_number", null);
            }
        }

        // Schema 1.x: provider groups written inline on the rate. Give them a private (negative) reference id.
        if (block.ProviderGroups is { Count: > 0 } inlineGroups)
        {
            var inlineId = _nextInlineReferenceId--;
            _definedReferences.Add(inlineId);
            _usedReferences.Add(inlineId);
            AddGroups(inlineId, inlineGroups, null);
            references.Add(inlineId);
        }

        var result = references.Distinct().ToArray();
        Array.Sort(result);
        return result;
    }

    private void AddGroups(int referenceId, List<ProviderGroup>? groups, string? networks)
    {
        if (groups is null || groups.Count == 0)
        {
            Reject("provider_reference_without_groups", referenceId.ToString(CultureInfo.InvariantCulture));
            return;
        }

        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            var tinType = Intern(Lower(group.Tin?.Type) ?? "");
            var tinValue = Clean(group.Tin?.Value);
            var businessName = Clean(group.Tin?.BusinessName);

            string? ein = null;
            if (tinType == "ein")
            {
                ein = Ein.Normalize(tinValue);
                if (ein is null)
                {
                    Reject("ein_not_nine_digits", tinValue);
                }
                else
                {
                    ein = Intern(ein);
                    _distinctEins.Add(ein);
                }
            }

            var npiCount = 0;
            foreach (var value in group.Npi ?? [])
            {
                if (value is not { } npi)
                {
                    Reject("npi_not_a_number", null);
                    continue;
                }

                // CMS uses [0] to mean "contract is with the whole TIN; no individual NPIs listed".
                if (npi == 0)
                {
                    continue;
                }

                npiCount++;
                var valid = Npi.IsValid(npi);
                if (!valid)
                {
                    _invalidNpis.Add(npi);
                }

                _distinctNpis.Add(npi);
                _groupNpis.Add(new GroupNpiRecord(referenceId, groupIndex, npi, valid));
            }

            var einLevel = npiCount == 0;
            var unverified = ein is not null && Ein.HasNeverIssuedPrefix(ein);
            if (einLevel)
            {
                _einLevelGroups++;
            }

            if (unverified)
            {
                _unverifiedEinGroups++;
            }

            _groups.Add(new ProviderGroupRecord(
                referenceId, groupIndex, tinType, tinValue, ein, businessName is null ? null : Intern(businessName),
                npiCount, einLevel, unverified, networks));
        }
    }

    private void RememberDescription(string codeType, string code, InNetworkItem item)
    {
        var key = (codeType, code);
        if (_descriptions.ContainsKey(key))
        {
            return;
        }

        var name = Clean(item.Name);
        var description = Clean(item.Description);
        if (name is null && description is null)
        {
            return;
        }

        _descriptions[key] = new CodeDescriptionRecord(codeType, code, name, description);
    }

    private string[] NormalizePlaces(List<string?>? serviceCodes)
    {
        if (serviceCodes is null || serviceCodes.Count == 0)
        {
            return [];
        }

        var places = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var raw in serviceCodes)
        {
            var value = (raw ?? "").Trim().ToUpperInvariant();
            if (value.Length == 0)
            {
                continue;
            }

            // Place-of-service codes are two digits; "2" written as a number becomes "02".
            places.Add(Intern(value.Length == 1 && char.IsAsciiDigit(value[0]) ? "0" + value : value));
        }

        return [.. places];
    }

    private static string NormalizeModifiers(List<string?>? modifiers)
    {
        if (modifiers is null || modifiers.Count == 0)
        {
            return "";
        }

        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var raw in modifiers)
        {
            var value = (raw ?? "").Trim().ToUpperInvariant();
            if (value.Length > 0)
            {
                set.Add(value);
            }
        }

        return string.Join(',', set);
    }

    /// <summary>9999-12-31 is CMS's "no end date" marker and is stored as null.</summary>
    private static DateOnly? ParseExpiration(string? text)
    {
        var date = ParseDate(text);
        return date is { Year: 9999 } ? null : date;
    }

    internal static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParseExact((text ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private void Reject(string reason, string? example, long count = 1, bool affectsPrices = false)
    {
        if (!_rejects.TryGetValue(reason, out var tally))
        {
            tally = new RejectTally(reason, affectsPrices);
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

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string? Lower(string? value) => Clean(value)?.ToLowerInvariant();
}

internal static class CodeSources
{
    /// <summary>The searchable code is the item's own billing code.</summary>
    public const string BillingCode = "billing_code";

    /// <summary>The searchable code was read from additional_information on a revenue-code (facility) row.</summary>
    public const string AdditionalInformation = "additional_information";
}

/// <summary>Everything that makes two published prices the same fact.</summary>
internal readonly record struct RateKey(
    string BillingCodeType,
    string BillingCode,
    string CodeType,
    string Code,
    string Arrangement,
    string NegotiatedType,
    string BillingClass,
    string Setting,
    decimal Rate,
    DateOnly? Expiration,
    string Places,
    string Modifiers,
    string AdditionalInformation,
    string ProviderReferences);

internal sealed class RateEntry(
    RateKey key,
    string billingCodeRaw,
    string codeSource,
    string[] places,
    int[] providerReferences,
    int itemIndex,
    int rateIndex,
    int priceIndex)
{
    private HashSet<string>? _labels;

    public RateKey Key { get; } = key;
    public string BillingCodeRaw { get; } = billingCodeRaw;
    public string CodeSource { get; } = codeSource;
    public string[] Places { get; } = places;
    public int[] ProviderReferences { get; } = providerReferences;

    /// <summary>JSON path of the first occurrence, for source traceability.</summary>
    public int ItemIndex { get; } = itemIndex;
    public int RateIndex { get; } = rateIndex;
    public int PriceIndex { get; } = priceIndex;

    /// <summary>How many published rows collapsed into this one.</summary>
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

internal sealed record ProviderGroupRecord(
    int ProviderReferenceId,
    int GroupIndex,
    string TinType,
    string? TinValue,
    string? Ein,
    string? BusinessName,
    int NpiCount,
    bool EinLevel,
    bool EinUnverified,
    string? NetworkNames);

internal sealed record GroupNpiRecord(int ProviderReferenceId, int GroupIndex, long Npi, bool Valid);

internal sealed record CodeDescriptionRecord(string CodeType, string Code, string? Name, string? Description);

internal sealed class RejectTally(string reason, bool affectsPrices)
{
    public string Reason { get; } = reason;
    public bool AffectsPrices { get; } = affectsPrices;
    public long Count { get; set; }
    public string? Example { get; set; }
}

internal sealed record FileStats(
    int Items,
    long PricesRaw,
    int RatesUnique,
    int ProviderReferences,
    int ProviderGroups,
    int DistinctNpis,
    int DistinctEins,
    int InvalidNpis,
    int EinLevelGroups,
    int UnverifiedEinGroups,
    int DanglingReferences,
    long RejectedPrices)
{
    public long DuplicatesRemoved => PricesRaw - RejectedPrices - RatesUnique;
}
