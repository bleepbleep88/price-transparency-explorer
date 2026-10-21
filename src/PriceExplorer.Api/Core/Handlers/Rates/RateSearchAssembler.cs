using System.Globalization;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Ingestion;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Models.Statistics;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Api.Core.Handlers.Rates;

/// <summary>
/// One unique rate, at one place of service, for one provider (tax ID). Rows come from SearchRates.sql, which explains
/// each match flag; flags for filters that aren't set are true.
/// </summary>
internal sealed record RateFactRow(
    long RateId,
    string ProductKey,
    string ProductLabel,
    string CodeType,
    string Code,
    string CodeSource,
    string BillingCodeType,
    string BillingCode,
    string BillingClass,
    string NegotiatedType,
    string Modifiers,
    decimal Rate,
    int Occurrences,
    string Pos,
    string ProviderKey,
    string? Ein,
    bool EinUnverified,
    bool EinLevel,
    string? BusinessNames,
    int NpiCount,
    bool FilterMatch,
    bool IdMatch = true,
    bool NameMatch = true,
    bool NpiMatch = true,
    bool EinMatch = true,
    string? MatchedBusinessNames = null,
    bool SpecialtyMatch = true,
    bool CountyMatch = true,
    bool WithoutName = true,
    bool WithoutSpecialty = true,
    bool WithoutCounty = true,
    bool WithoutDirectory = true,
    string? ListedAs = null);

internal sealed record AssemblyOptions(
    string? Product,
    string? Setting,
    string? FeeType,
    string? CodeType,
    string? Group,
    bool ProviderFilterActive,
    string Sort,
    int Page,
    int PageSize);

internal sealed record AssembledSearch(
    IReadOnlyList<RateGroup> Groups,
    RateRowsPage Rows,
    SearchQuality Quality,
    IReadOnlyList<RateGroup> AllGroups);

/// <summary>
/// Pure grouping and statistics for a search: builds fair comparison groups (one value per provider), applies the
/// UI filters, ranks providers within their group, and pages the rows. No database access, so it's unit-tested directly.
/// <see cref="Summarize"/> gives the same groups and values whole, for the saved statistics tables.
/// </summary>
internal static class RateSearchAssembler
{
    public static AssembledSearch Assemble(IReadOnlyList<RateFactRow> facts, AssemblyOptions options, ReferenceCatalog catalog)
    {
        var built = Accumulate(facts, catalog)
            .Select(accumulator =>
            {
                var distribution = Distribution.Of(accumulator);
                return (Accumulator: accumulator, Distribution: distribution, Group: Build(accumulator, distribution, catalog, options.ProviderFilterActive));
            })
            .OrderBy(pair => pair.Group.ProductLabel, StringComparer.Ordinal)
            .ThenByDescending(pair => pair.Group.ProviderCount)
            .ThenBy(pair => pair.Accumulator.Setting.SortOrder)
            .ThenBy(pair => pair.Group.Key, StringComparer.Ordinal)
            .ToList();

        var visible = built
            .Where(pair => options.Product is null || pair.Group.ProductKey == options.Product)
            .Where(pair => options.Setting is null || pair.Group.SettingKey == options.Setting)
            .Where(pair => options.FeeType is null || pair.Group.FeeType == options.FeeType)
            .Where(pair => options.CodeType is null || pair.Group.CodeType == options.CodeType)
            .Where(pair => !options.ProviderFilterActive || pair.Accumulator.Providers.Values.Any(p => p.Matched))
            .ToList();

        var rows = visible
            .Where(pair => options.Group is null || pair.Group.Key == options.Group)
            .SelectMany(pair => RowsFor(pair.Accumulator, pair.Distribution, pair.Group, options.ProviderFilterActive))
            .ToList();

        var skip = (long)(options.Page - 1) * options.PageSize;
        var page = skip >= rows.Count
            ? []
            : Sort(rows, options.Sort).Skip((int)skip).Take(options.PageSize).ToList();

        return new AssembledSearch(
            visible.Select(pair => pair.Group).ToList(),
            new RateRowsPage(rows.Count, options.Page, options.PageSize, page),
            QualityOf(visible.Select(pair => pair.Accumulator).ToList()),
            built.Select(pair => pair.Group).ToList());
    }

    /// <summary>
    /// Every group's statistics and every provider's value in it, exactly as a search with no provider filter shows them,
    /// with no filters or paging: what the statistics tables save. Groups come in key order, providers in key order.
    /// </summary>
    public static IReadOnlyList<GroupStats> Summarize(IReadOnlyList<RateFactRow> facts, ReferenceCatalog catalog) =>
        Accumulate(facts, catalog)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var distribution = Distribution.Of(group);
                var (unit, perUnit) = PricingOf(group, ServiceCategories.Classify(group.CodeType, group.Code));
                return new GroupStats(
                    group.Key, group.ProductKey, group.ProductLabel, group.CodeType, group.Code, group.FeeType, group.Setting.Key,
                    group.Modifiers, group.NegotiatedType, unit, perUnit, distribution.Values.Count, group.RateIds.Count,
                    distribution.Level, distribution.Stats, distribution.LowerQuartileGap,
                    group.Providers.Values.OrderBy(provider => provider.Key, StringComparer.Ordinal).Select(ValueOf).ToList());
            })
            .ToList();

    /// <summary>The comparison group a fact belongs to: product, code, fee type, setting, modifiers and rate type.</summary>
    public static string GroupKeyOf(RateFactRow fact, ReferenceCatalog catalog) =>
        string.Join('|', fact.ProductKey, fact.CodeType, fact.Code, FeeTypeOf(fact.BillingClass), catalog.SettingForPlace(fact.Pos).Key,
            fact.Modifiers, fact.NegotiatedType);

    public static string FeeTypeOf(string billingClass) => billingClass switch
    {
        "professional" => "professional",
        "institutional" => "facility",
        _ => billingClass,
    };

    public static string FeeTypeLabelOf(string feeType) => feeType switch
    {
        "facility" => "Facility fee",
        "professional" => "Professional (clinician) fee",
        _ => "Fee",
    };

    /// <summary>Gathers facts into comparison groups, each provider once per group with all its rates there.</summary>
    private static IEnumerable<GroupAccumulator> Accumulate(IReadOnlyList<RateFactRow> facts, ReferenceCatalog catalog)
    {
        var accumulators = new Dictionary<string, GroupAccumulator>(StringComparer.Ordinal);
        foreach (var fact in facts)
        {
            var key = GroupKeyOf(fact, catalog);
            if (!accumulators.TryGetValue(key, out var accumulator))
            {
                accumulator = new GroupAccumulator(key, fact, FeeTypeOf(fact.BillingClass), catalog.SettingForPlace(fact.Pos));
                accumulators[key] = accumulator;
            }

            accumulator.Add(fact);
        }

        return accumulators.Values;
    }

    /// <summary>A provider's value over all its rates in the group: what an unfiltered row shows and the statistics count.</summary>
    private static ProviderValue ValueOf(ProviderAccumulator provider)
    {
        var view = provider.AllRates;
        return new ProviderValue(
            provider.Key, provider.VerifiedEin, provider.EinDisplay, provider.EinUnverified, view.WholeTaxId,
            provider.NameFor(view, filtered: false), view.Value, view.Low, view.High, view.RateIds.Count, view.NpiWeight,
            view.SourceRateId, view.WholeTaxIdRateId, view.RateSet);
    }

    private static RateGroup Build(GroupAccumulator group, Distribution distribution, ReferenceCatalog catalog, bool providerFilterActive)
    {
        var values = distribution.Values;
        var median = distribution.Stats.Median;
        var p25 = distribution.Stats.P25;

        var category = ServiceCategories.Classify(group.CodeType, group.Code);
        var (unit, perUnit) = PricingOf(group, category);
        var modifiers = group.Modifiers.Length == 0
            ? []
            : group.Modifiers.Split(',').Select(code => new ModifierRef(code, catalog.Modifier(code).Label)).ToList();

        // Positions are only meaningful when a provider filter picked someone out; otherwise everyone "matches".
        // A matched provider is placed by its matched rates (an NPI's own rate), against everyone else's market value.
        List<ProviderPosition> highlights = !providerFilterActive
            ? []
            : group.Providers.Values
                .Where(provider => provider.Matched)
                .Select(provider => (Provider: provider, View: provider.MatchedRates))
                .OrderBy(pair => pair.View.Value)
                .ThenBy(pair => pair.Provider.Key, StringComparer.Ordinal)
                .Take(5)
                .Select(pair => new ProviderPosition(
                    pair.Provider.Key,
                    pair.Provider.NameFor(pair.View, filtered: true),
                    pair.View.Value,
                    distribution.Basic ? RateStatistics.PercentRank(values, pair.View.Value, pair.Provider.Value) : null,
                    RateStatistics.Ratio(pair.View.Value, median),
                    p25 is { } low ? pair.View.Value - low : null,
                    pair.View.Low,
                    pair.View.High))
                .ToList();

        return new RateGroup(
            Key: group.Key,
            ProductKey: group.ProductKey,
            ProductLabel: group.ProductLabel,
            CodeType: group.CodeType,
            Code: group.Code,
            FeeType: group.FeeType,
            FeeTypeLabel: FeeTypeLabelOf(group.FeeType),
            SettingKey: group.Setting.Key,
            SettingLabel: group.Setting.Label,
            SettingNote: ClinicPerUnit(group, perUnit) ? null : group.Setting.Note,
            Places: group.Places.Select(pos => new PlaceRef(pos, catalog.PlaceName(pos))).ToList(),
            Modifiers: modifiers,
            NegotiatedType: group.NegotiatedType,
            Unit: unit,
            PerUnit: perUnit,
            BilledAs: group.BilledAs
                .Select(billed => new BilledAs(billed.Type, billed.Code, LabelFor(billed.Type, billed.Code, catalog)))
                .ToList(),
            ProviderCount: values.Count,
            RateCount: group.RateIds.Count,
            StatsLevel: distribution.Level,
            Stats: distribution.Stats,
            LowerQuartileGap: distribution.LowerQuartileGap,
            ListedRates: distribution.Level == "list" ? values : [],
            Highlights: highlights,
            MatchedProviders: providerFilterActive ? group.Providers.Values.Count(provider => provider.Matched) : 0,
            Notes: NotesFor(group, category, perUnit, catalog, values));
    }

    private static IEnumerable<RateRow> RowsFor(GroupAccumulator accumulator, Distribution distribution, RateGroup group, bool providerFilterActive)
    {
        var values = distribution.Values;

        foreach (var provider in accumulator.Providers.Values)
        {
            if (providerFilterActive && !provider.Matched)
            {
                continue;
            }

            // With a provider filter the row shows only the matched rates: an NPI's own rate, not its tax ID's blend. The
            // name is the facility actually paid the rate shown (a tax ID can run several), so the row and its source agree.
            var view = providerFilterActive ? provider.MatchedRates : provider.AllRates;
            var name = provider.NameFor(view, providerFilterActive);
            string? band = null;
            if (distribution.Basic && distribution.Stats is { P25: { } p25, Median: { } median, P75: { } p75 })
            {
                band = RateStatistics.Band(view.Value, p25, median, p75);
            }

            yield return new RateRow(
                GroupKey: group.Key,
                ProviderKey: provider.Key,
                Ein: provider.VerifiedEin,
                EinDisplay: provider.EinDisplay,
                EinMasked: provider.EinUnverified,
                EinLevel: view.WholeTaxId,
                BusinessName: name,
                OtherNames: provider.NamesOtherThan(name),
                Rate: view.Value,
                RateLow: view.Low,
                RateHigh: view.High,
                MultipleRates: view.High != view.Low,
                VsMedian: RateStatistics.Ratio(view.Value, distribution.Stats.Median),
                Percentile: distribution.Basic ? RateStatistics.PercentRank(values, view.Value, provider.Value) : null,
                Band: band,
                NpiCount: view.LargestNpiCount,
                Occurrences: view.Occurrences,
                RateCount: view.RateIds.Count,
                FirstRateId: view.SourceRateId,
                Matched: provider.Matched,
                RateIds: view.RateIds,
                WholeTaxIdRateId: view.WholeTaxIdRateId)
            {
                ListedAs = provider.ListedAsFor(name, view.SourceRateId),
            };
        }
    }

    /// <summary>Every sort ends on group and provider keys, so equal values keep one stable order and pages never overlap.</summary>
    private static IEnumerable<RateRow> Sort(IEnumerable<RateRow> rows, string sort)
    {
        var sorted = sort switch
        {
            "-rate" => rows.OrderByDescending(r => r.Rate).ThenBy(r => r.BusinessName, StringComparer.OrdinalIgnoreCase),
            "provider" => rows.OrderBy(r => r.BusinessName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Rate),
            "-provider" => rows.OrderByDescending(r => r.BusinessName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Rate),
            "vsMedian" => rows.OrderBy(r => r.VsMedian ?? decimal.MaxValue).ThenBy(r => r.Rate).ThenBy(r => r.BusinessName, StringComparer.OrdinalIgnoreCase),
            "-vsMedian" => rows.OrderByDescending(r => r.VsMedian ?? decimal.MinValue).ThenBy(r => r.Rate).ThenBy(r => r.BusinessName, StringComparer.OrdinalIgnoreCase),
            _ => rows.OrderBy(r => r.Rate).ThenBy(r => r.BusinessName, StringComparer.OrdinalIgnoreCase),
        };

        return sorted.ThenBy(r => r.GroupKey, StringComparer.Ordinal).ThenBy(r => r.ProviderKey, StringComparer.Ordinal);
    }

    private static SearchQuality QualityOf(IReadOnlyList<GroupAccumulator> groups)
    {
        // Every provider counter is distinct tax IDs, even when a provider shows up in several groups.
        var occurrencesByRate = new Dictionary<long, int>();
        var masked = new HashSet<string>(StringComparer.Ordinal);
        var wholeTin = new HashSet<string>(StringComparer.Ordinal);
        var multiple = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            foreach (var (rateId, occurrences) in group.RateIds)
            {
                occurrencesByRate[rateId] = occurrences;
            }

            foreach (var provider in group.Providers.Values)
            {
                if (provider.AllRates.High != provider.AllRates.Low) multiple.Add(provider.Key);
                if (provider.EinUnverified) masked.Add(provider.Key);
                if (provider.AllRates.WholeTaxId) wholeTin.Add(provider.Key);
            }
        }

        var published = occurrencesByRate.Values.Sum(o => (long)o);
        return new SearchQuality(published, occurrencesByRate.Count, published - occurrencesByRate.Count, multiple.Count, masked.Count, wholeTin.Count);
    }

    private static List<string> NotesFor(
        GroupAccumulator group, ServiceCategory category, bool perUnit, ReferenceCatalog catalog, IReadOnlyList<decimal> values)
    {
        var notes = new List<string>();

        // When at least half the providers are paid one identical amount, it's a standard fee schedule, not a negotiated spread.
        if (values.Count >= RateStatistics.MinimumForBasicStats)
        {
            var counts = values.GroupBy(value => value).Select(same => (Amount: same.Key, Count: same.Count())).ToList();
            var top = counts.Max(amount => amount.Count);
            if (top * 2 >= values.Count)
            {
                // Two amounts can each hold exactly half: name both.
                var modes = counts.Where(amount => amount.Count == top).Select(amount => amount.Amount).Order().ToList();
                notes.Add(modes.Count == 1
                    ? $"{top} of {values.Count} providers get exactly the same price: {RateSearchFormat.Money(modes[0])}."
                    : $"The {values.Count} providers are split evenly: {top} get exactly {RateSearchFormat.Money(modes[0])} " +
                      $"and {top} get exactly {RateSearchFormat.Money(modes[1])}.");
            }
        }

        foreach (var code in group.Modifiers.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var modifier = catalog.Modifier(code);
            if (modifier.Component == "professional")
            {
                notes.Add("Doctor's interpretation only (modifier 26): not the full price of the service.");
            }
            else if (modifier.Component == "technical")
            {
                notes.Add("Equipment and staff only (modifier TC): not the full price of the service.");
            }
        }

        // A clinic's all-inclusive visit note doesn't apply to a drug or supply priced per unit there.
        if (group.Setting.Note is { } settingNote && !ClinicPerUnit(group, perUnit))
        {
            notes.Add(settingNote);
        }

        var facilityRows = group.BilledAs.Where(b => b.Type == BillingCodes.RevenueCode && group.CodeType != BillingCodes.RevenueCode).ToList();
        if (facilityRows.Count > 0)
        {
            var codes = string.Join(", ", facilityRows.Select(b => $"{b.Code} ({catalog.RevenueCodeLabel(b.Code)})"));
            notes.Add($"The plan files these facility rates under revenue code{(facilityRows.Count == 1 ? "" : "s")} {codes}, naming {group.CodeType} {group.Code} in the notes field" +
                (category.PerUnit && !perUnit ? $": they are facility charges for the case, not prices {category.Unit}." : "."));
        }

        if (perUnit)
        {
            notes.Add($"Priced {category.Unit}, not per visit or procedure.");
        }

        if (ServiceCategories.LevelFamily(group.CodeType, group.Code) is { } family)
        {
            // How often a level gets billed is said once, in the page footer.
            notes.Add($"{family}: compare level to level only.");
        }

        if (group.NegotiatedType != "negotiated")
        {
            notes.Add(group.NegotiatedType == "percentage"
                ? "Rate is a percentage of billed charges, not a dollar amount."
                : $"Rate type: {group.NegotiatedType}.");
        }

        return notes;
    }

    private static string LabelFor(string billingCodeType, string billingCode, ReferenceCatalog catalog) => billingCodeType switch
    {
        BillingCodes.RevenueCode => $"Revenue code {billingCode} ({catalog.RevenueCodeLabel(billingCode)})",
        _ => $"{billingCodeType} {billingCode}",
    };

    /// <summary>
    /// What one of the group's rates pays for, and whether it's a price per unit. A drug named in the notes of a surgery
    /// or room revenue code line is that facility's charge for the case, not a price per unit (J0585 under revenue code
    /// 0490 is a surgery center's charge, not Botox per unit). Pharmacy and clinic lines keep their per-unit price
    /// (J9190 at $2.01 under a clinic revenue code).
    /// </summary>
    private static (string Unit, bool PerUnit) PricingOf(GroupAccumulator group, ServiceCategory category)
    {
        var perUnit = category.PerUnit && !group.FiledAsCaseRate;
        var unit = category.PerUnit && !perUnit ? "per facility charge"
            : group.Setting.Key is "fqhc" or "rhc" && !perUnit ? ServiceCategories.PerVisitAllInclusive
            : category.Unit;
        return (unit, perUnit);
    }

    /// <summary>A drug or supply priced per unit at a health center or rural clinic, where visits are paid all-inclusive.</summary>
    private static bool ClinicPerUnit(GroupAccumulator group, bool perUnit) => perUnit && group.Setting.Key is "fqhc" or "rhc";

    private static readonly HashSet<string> CaseRateRevenueCodes = new(["036", "045", "049", "071", "075", "076", "079"], StringComparer.Ordinal);

    /// <summary>
    /// A group's provider values and their statistics: linear-interpolation quantiles rounded to cents, median and
    /// quartiles from 5 providers, p10/p90, spread and the lower-quartile gap from 10. Search cards, search rows and the
    /// saved statistics all take their numbers from here.
    /// </summary>
    /// <param name="Values">One value per provider, in ascending order.</param>
    private sealed record Distribution(IReadOnlyList<decimal> Values, string Level, RateStats Stats, decimal? LowerQuartileGap)
    {
        public bool Basic => Values.Count >= RateStatistics.MinimumForBasicStats;

        public static Distribution Of(GroupAccumulator group)
        {
            var values = group.Providers.Values.Select(provider => provider.Value).Order().ToList();
            var basic = values.Count >= RateStatistics.MinimumForBasicStats;
            var full = values.Count >= RateStatistics.MinimumForFullStats;

            decimal? median = basic ? RateStatistics.Median(values) : null;
            decimal? p25 = basic ? RateStatistics.Quantile(values, 0.25) : null;
            decimal? p75 = basic ? RateStatistics.Quantile(values, 0.75) : null;
            decimal? p10 = full ? RateStatistics.Quantile(values, 0.10) : null;
            decimal? p90 = full ? RateStatistics.Quantile(values, 0.90) : null;
            var stats = new RateStats(
                values[0], p10, p25, median, p75, p90, values[^1],
                full ? RateStatistics.Ratio(values[^1], values[0]) : null,
                full && p90 is { } top ? RateStatistics.Ratio(top, p10) : null);

            return new Distribution(
                values, RateStatistics.LevelFor(values.Count), stats, full && median is { } m && p25 is { } q ? m - q : null);
        }
    }

    private sealed class GroupAccumulator(string key, RateFactRow first, string feeType, SettingInfo setting)
    {
        public string Key { get; } = key;
        public string ProductKey { get; } = first.ProductKey;
        public string ProductLabel { get; } = first.ProductLabel;
        public string CodeType { get; } = first.CodeType;
        public string Code { get; } = first.Code;
        public string FeeType { get; } = feeType;
        public SettingInfo Setting { get; } = setting;
        public string Modifiers { get; } = first.Modifiers;
        public string NegotiatedType { get; } = first.NegotiatedType;
        public SortedSet<string> Places { get; } = new(StringComparer.Ordinal);
        public SortedSet<(string Type, string Code)> BilledAs { get; } = [];

        /// <summary>
        /// Every rate in the group is filed under a surgery or room revenue code (operating room 036x, emergency room 045x,
        /// ambulatory surgery 049x, recovery 071x, GI services 075x, treatment and observation rooms 076x, lithotripsy
        /// 079x), with this code only in the notes field: a charge for the case, not for the item named.
        /// </summary>
        public bool FiledAsCaseRate =>
            CodeType != BillingCodes.RevenueCode
            && BilledAs.All(billed => billed.Type == BillingCodes.RevenueCode && CaseRateRevenueCodes.Contains(billed.Code[..3]));
        public Dictionary<long, int> RateIds { get; } = [];
        public Dictionary<string, ProviderAccumulator> Providers { get; } = new(StringComparer.Ordinal);

        public void Add(RateFactRow fact)
        {
            if (fact.Pos.Length > 0)
            {
                Places.Add(fact.Pos);
            }

            BilledAs.Add((fact.BillingCodeType, fact.BillingCode));
            RateIds[fact.RateId] = fact.Occurrences;

            if (!Providers.TryGetValue(fact.ProviderKey, out var provider))
            {
                provider = new ProviderAccumulator(fact.ProviderKey, fact.Ein, fact.EinUnverified);
                Providers[fact.ProviderKey] = provider;
            }

            provider.Add(fact);
        }
    }

    private sealed class ProviderAccumulator(string key, string? ein, bool einUnverified)
    {
        private readonly SortedSet<string> _names = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _matchedNames = new(StringComparer.Ordinal);
        private readonly Dictionary<long, SortedSet<string>> _namesByRate = [];
        private readonly Dictionary<long, SortedSet<string>> _matchedNamesByRate = [];
        private readonly Dictionary<long, List<(string Published, string Shown)>> _listedByRate = [];
        private readonly Dictionary<long, RateEntry> _rates = [];
        private RateView? _all;
        private RateView? _matched;

        public string Key { get; } = key;
        public string? Ein { get; } = ein;
        public bool EinUnverified { get; private set; } = einUnverified;
        public bool Matched { get; private set; }

        /// <summary>Every rate the provider has in the group.</summary>
        public RateView AllRates => _all ??= RateView.Of(_rates.Values);

        /// <summary>Only the rates that matched the provider filters (every rate when no filter is set, since all match).</summary>
        public RateView MatchedRates => _matched ??= MatchedOrAll();

        /// <summary>One value per provider per group for the statistics (see <see cref="RateView.Value"/>).</summary>
        public decimal Value => AllRates.Value;

        public string DisplayName => _names.Count > 0 ? _names.Min! : "Name not published";

        /// <summary>
        /// The name on the provider groups that matched the filters. A tax ID with several facilities (a hospital system)
        /// lists each under its own name; the matched facility's name belongs next to the matched rate.
        /// </summary>
        public string MatchedDisplayName => _matchedNames.Count > 0 ? _matchedNames.Min! : DisplayName;

        /// <summary>
        /// The name on this provider's group(s) behind the rate a row shows (<see cref="RateView.SourceRateId"/>, the one
        /// the source drawer opens), among the groups that matched when a provider filter is on.
        /// </summary>
        public string NameFor(RateView view, bool filtered) =>
            filtered && _matchedNamesByRate.TryGetValue(view.SourceRateId, out var matched) && matched.Count > 0 ? matched.Min!
            : _namesByRate.TryGetValue(view.SourceRateId, out var names) && names.Count > 0 ? names.Min!
            : filtered ? MatchedDisplayName : DisplayName;

        public IReadOnlyList<string> NamesOtherThan(string name) => _names.Where(other => other != name).ToList();

        /// <summary>
        /// The plan's name(s) as published behind a shown name that replaced them (a network's shared name, or a name from
        /// an unreliable file), from the rate the row shows, else any of its rates. Null when the plan's name is shown.
        /// </summary>
        public string? ListedAsFor(string shown, long sourceRateId)
        {
            var pairs = _listedByRate.TryGetValue(sourceRateId, out var onRate) && onRate.Any(pair => pair.Shown == shown)
                ? onRate
                : _listedByRate.Values.SelectMany(rate => rate);
            var published = pairs.Where(pair => pair.Shown == shown).Select(pair => pair.Published).Distinct().Order(StringComparer.Ordinal).ToList();
            return published.Count > 0 ? string.Join("; ", published) : null;
        }

        /// <summary>The EIN when it can be shown: null when masked (a prefix the IRS never issues).</summary>
        public string? VerifiedEin => EinUnverified ? null : Ein;

        public string EinDisplay => ProviderKeys.Display(Key, Ein, EinUnverified);

        private RateView MatchedOrAll()
        {
            var matched = _rates.Values.Where(rate => rate.Matched).ToList();
            return matched.Count > 0 ? RateView.Of(matched) : AllRates;
        }

        public void Add(RateFactRow fact)
        {
            _all = null;
            _matched = null;

            // The same rate can arrive once per place of service that maps to this group's setting.
            if (_rates.TryGetValue(fact.RateId, out var existing))
            {
                existing.Matched |= fact.FilterMatch;
                existing.WholeTaxId |= fact.EinLevel;
            }
            else
            {
                _rates[fact.RateId] = new RateEntry(fact.RateId, fact.Rate, fact.Occurrences, fact.NpiCount)
                {
                    Matched = fact.FilterMatch,
                    WholeTaxId = fact.EinLevel,
                };
            }

            Matched |= fact.FilterMatch;
            EinUnverified |= fact.EinUnverified;
            foreach (var name in SplitNames(fact.BusinessNames))
            {
                _names.Add(name);
                NamesOf(_namesByRate, fact.RateId).Add(name);
            }

            // "published␟shown" pairs split by ␞ (chr(31) and chr(30) in SearchRates.sql).
            foreach (var pair in (fact.ListedAs ?? "").Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('\u001f');
                if (parts.Length == 2)
                {
                    if (!_listedByRate.TryGetValue(fact.RateId, out var listed))
                    {
                        listed = [];
                        _listedByRate[fact.RateId] = listed;
                    }

                    listed.Add((parts[0], parts[1]));
                }
            }

            if (fact.FilterMatch)
            {
                foreach (var name in SplitNames(fact.MatchedBusinessNames))
                {
                    _matchedNames.Add(name);
                    NamesOf(_matchedNamesByRate, fact.RateId).Add(name);
                }
            }
        }

        private static SortedSet<string> NamesOf(Dictionary<long, SortedSet<string>> byRate, long rateId)
        {
            if (!byRate.TryGetValue(rateId, out var names))
            {
                names = new SortedSet<string>(StringComparer.Ordinal);
                byRate[rateId] = names;
            }

            return names;
        }

        private static string[] SplitNames(string? names) =>
            (names ?? "").Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private sealed class RateEntry(long rateId, decimal rate, int occurrences, int npiCount)
    {
        public long RateId { get; } = rateId;
        public decimal Rate { get; } = rate;
        public int Occurrences { get; } = occurrences;
        public int NpiCount { get; } = npiCount;
        public bool Matched { get; set; }

        /// <summary>The rate comes (at least partly) from a contract with the whole tax ID: the file lists npi [0] for it.</summary>
        public bool WholeTaxId { get; set; }
    }

    /// <summary>A provider's rates in one group, summed up for display.</summary>
    /// <param name="Value">
    /// The NPI-weighted median of the rates: always one the provider is really paid (never a midpoint). Most providers
    /// have a single rate per group, so this is usually just that rate.
    /// </param>
    /// <param name="SourceRateId">The rate carrying <paramref name="Value"/> that lists the most NPIs (shown in the source drawer).</param>
    /// <param name="LargestNpiCount">NPIs under the largest single rate: a lower bound until the handler counts them exactly.</param>
    /// <param name="WholeTaxId">At least one of the rates comes from a contract with the whole tax ID (npi [0]).</param>
    /// <param name="WholeTaxIdRateId">Such a whole-tax-ID rate, when <paramref name="SourceRateId"/> isn't one.</param>
    /// <param name="NpiWeight">The total weight the weighted median used: each rate's NPI count, at least 1, summed.</param>
    /// <param name="RateSet">The distinct rates, ascending, as "0.0000" joined by commas: two files list the same rates
    /// exactly when these match, whatever the NPI counts behind them.</param>
    private sealed record RateView(
        decimal Value, decimal Low, decimal High, long SourceRateId, IReadOnlyList<long> RateIds, int Occurrences, int LargestNpiCount,
        bool WholeTaxId, long? WholeTaxIdRateId, long NpiWeight, string RateSet)
    {
        public static RateView Of(IEnumerable<RateEntry> entries)
        {
            var list = entries.OrderBy(entry => entry.RateId).ToList();
            var value = RateStatistics.WeightedMedian(list.Select(entry => (entry.Rate, entry.NpiCount)));
            var source = list.Where(entry => entry.Rate == value).OrderByDescending(entry => entry.NpiCount).ThenBy(entry => entry.RateId).First();
            return new RateView(
                value,
                list.Min(entry => entry.Rate),
                list.Max(entry => entry.Rate),
                source.RateId,
                list.Select(entry => entry.RateId).ToList(),
                list.Sum(entry => entry.Occurrences),
                list.Max(entry => entry.NpiCount),
                list.Any(entry => entry.WholeTaxId),
                source.WholeTaxId ? null : list.FirstOrDefault(entry => entry.WholeTaxId)?.RateId,
                list.Sum(entry => RateStatistics.MedianWeight(entry.NpiCount)),
                string.Join(',', list.Select(entry => entry.Rate).Distinct().Order()
                    .Select(rate => rate.ToString("0.0000", CultureInfo.InvariantCulture))));
        }
    }
}

internal static class RateSearchFormat
{
    public static string Money(decimal value) => value.ToString("$#,##0.00", CultureInfo.InvariantCulture);

    public static string SnapshotDate(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "selected";
}
