using System.Globalization;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Ingestion;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Api.Core.Handlers.Rates;

/// <summary>One unique rate, at one place of service, for one provider (tax ID). Rows come from SearchRates.sql.</summary>
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
    bool NameMatch = true);

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
/// </summary>
internal static class RateSearchAssembler
{
    public static AssembledSearch Assemble(IReadOnlyList<RateFactRow> facts, AssemblyOptions options, ReferenceCatalog catalog)
    {
        var accumulators = new Dictionary<string, GroupAccumulator>(StringComparer.Ordinal);
        foreach (var fact in facts)
        {
            var setting = catalog.SettingForPlace(fact.Pos);
            var feeType = FeeTypeOf(fact.BillingClass);
            var key = string.Join('|', fact.ProductKey, fact.CodeType, fact.Code, feeType, setting.Key, fact.Modifiers, fact.NegotiatedType);
            if (!accumulators.TryGetValue(key, out var accumulator))
            {
                accumulator = new GroupAccumulator(key, fact, feeType, setting);
                accumulators[key] = accumulator;
            }

            accumulator.Add(fact);
        }

        var built = accumulators.Values
            .Select(accumulator => (Accumulator: accumulator, Group: Build(accumulator, catalog, options.ProviderFilterActive)))
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
            .SelectMany(pair => RowsFor(pair.Accumulator, pair.Group, options.ProviderFilterActive))
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

    public static string FeeTypeOf(string billingClass) => billingClass switch
    {
        "professional" => "professional",
        "institutional" => "facility",
        _ => billingClass,
    };

    private static RateGroup Build(GroupAccumulator group, ReferenceCatalog catalog, bool providerFilterActive)
    {
        var values = group.Providers.Values.Select(p => p.Value).Order().ToList();
        var count = values.Count;
        var level = RateStatistics.LevelFor(count);
        var basic = count >= RateStatistics.MinimumForBasicStats;
        var full = count >= RateStatistics.MinimumForFullStats;

        decimal? median = basic ? RateStatistics.Median(values) : null;
        decimal? p25 = basic ? RateStatistics.Quantile(values, 0.25) : null;
        decimal? p75 = basic ? RateStatistics.Quantile(values, 0.75) : null;
        decimal? p10 = full ? RateStatistics.Quantile(values, 0.10) : null;
        decimal? p90 = full ? RateStatistics.Quantile(values, 0.90) : null;
        var stats = new RateStats(
            values[0], p10, p25, median, p75, p90, values[^1],
            full ? RateStatistics.Ratio(values[^1], values[0]) : null,
            full && p90 is { } top ? RateStatistics.Ratio(top, p10) : null);

        var category = ServiceCategories.Classify(group.CodeType, group.Code);
        var unit = group.Setting.Key is "fqhc" or "rhc" ? ServiceCategories.PerVisitAllInclusive : category.Unit;
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
                    pair.Provider.DisplayName,
                    pair.View.Value,
                    basic ? RateStatistics.PercentRank(values, pair.View.Value, pair.Provider.Value) : null,
                    RateStatistics.Ratio(pair.View.Value, median),
                    p25 is { } low ? pair.View.Value - low : null))
                .ToList();

        return new RateGroup(
            Key: group.Key,
            ProductKey: group.ProductKey,
            ProductLabel: group.ProductLabel,
            CodeType: group.CodeType,
            Code: group.Code,
            FeeType: group.FeeType,
            FeeTypeLabel: group.FeeType switch
            {
                "facility" => "Facility fee",
                "professional" => "Professional (clinician) fee",
                _ => "Fee",
            },
            SettingKey: group.Setting.Key,
            SettingLabel: group.Setting.Label,
            SettingNote: group.Setting.Note,
            Places: group.Places.Select(pos => new PlaceRef(pos, catalog.PlaceName(pos))).ToList(),
            Modifiers: modifiers,
            NegotiatedType: group.NegotiatedType,
            Unit: unit,
            PerUnit: category.PerUnit,
            BilledAs: group.BilledAs
                .Select(billed => new BilledAs(billed.Type, billed.Code, LabelFor(billed.Type, billed.Code, catalog)))
                .ToList(),
            ProviderCount: count,
            RateCount: group.RateIds.Count,
            StatsLevel: level,
            Stats: stats,
            SavingsPerCase: full && median is { } m && p25 is { } q ? m - q : null,
            ListedRates: level == "list" ? values : [],
            Highlights: highlights,
            Notes: NotesFor(group, category, catalog, values));
    }

    private static IEnumerable<RateRow> RowsFor(GroupAccumulator accumulator, RateGroup group, bool providerFilterActive)
    {
        var values = accumulator.Providers.Values.Select(p => p.Value).Order().ToList();
        var basic = values.Count >= RateStatistics.MinimumForBasicStats;

        foreach (var provider in accumulator.Providers.Values)
        {
            if (providerFilterActive && !provider.Matched)
            {
                continue;
            }

            // With a provider filter the row shows only the matched rates: an NPI's own rate, not its tax ID's blend.
            var view = providerFilterActive ? provider.MatchedRates : provider.AllRates;
            string? band = null;
            if (basic && group.Stats is { P25: { } p25, Median: { } median, P75: { } p75 })
            {
                band = RateStatistics.Band(view.Value, p25, median, p75);
            }

            yield return new RateRow(
                GroupKey: group.Key,
                ProviderKey: provider.Key,
                Ein: provider.EinUnverified ? null : provider.Ein,
                EinDisplay: provider.EinDisplay,
                EinMasked: provider.EinUnverified,
                EinLevel: provider.EinLevel,
                BusinessName: provider.DisplayName,
                OtherNames: provider.OtherNames,
                Rate: view.Value,
                RateLow: view.Low,
                RateHigh: view.High,
                MultipleRates: view.High != view.Low,
                VsMedian: RateStatistics.Ratio(view.Value, group.Stats.Median),
                Percentile: basic ? RateStatistics.PercentRank(values, view.Value, provider.Value) : null,
                Band: band,
                NpiCount: view.LargestNpiCount,
                Occurrences: view.Occurrences,
                RateCount: view.RateIds.Count,
                FirstRateId: view.SourceRateId,
                Matched: provider.Matched,
                RateIds: view.RateIds);
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
        var occurrencesByRate = new Dictionary<long, int>();
        var masked = new HashSet<string>(StringComparer.Ordinal);
        var wholeTin = new HashSet<string>(StringComparer.Ordinal);
        var multiple = 0;
        foreach (var group in groups)
        {
            foreach (var (rateId, occurrences) in group.RateIds)
            {
                occurrencesByRate[rateId] = occurrences;
            }

            foreach (var provider in group.Providers.Values)
            {
                if (provider.AllRates.High != provider.AllRates.Low) multiple++;
                if (provider.EinUnverified) masked.Add(provider.Key);
                if (provider.EinLevel) wholeTin.Add(provider.Key);
            }
        }

        var published = occurrencesByRate.Values.Sum(o => (long)o);
        return new SearchQuality(published, occurrencesByRate.Count, published - occurrencesByRate.Count, multiple, masked.Count, wholeTin.Count);
    }

    private static List<string> NotesFor(GroupAccumulator group, ServiceCategory category, ReferenceCatalog catalog, IReadOnlyList<decimal> values)
    {
        var notes = new List<string>();

        // When most providers are paid the identical amount, it's a standard fee schedule, not a negotiated spread.
        if (values.Count >= RateStatistics.MinimumForBasicStats)
        {
            var mode = values.GroupBy(value => value).OrderByDescending(same => same.Count()).First();
            if (mode.Count() * 2 >= values.Count)
            {
                notes.Add($"{mode.Count()} of {values.Count} providers are paid exactly {RateSearchFormat.Money(mode.Key)}: " +
                    "most likely a standard fee schedule rather than individually negotiated rates.");
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

        if (group.Setting.Note is { } settingNote)
        {
            notes.Add(settingNote);
        }

        var facilityRows = group.BilledAs.Where(b => b.Type == BillingCodes.RevenueCode && group.CodeType != BillingCodes.RevenueCode).ToList();
        if (facilityRows.Count > 0)
        {
            var codes = string.Join(", ", facilityRows.Select(b => $"{b.Code} ({catalog.RevenueCodeLabel(b.Code)})"));
            notes.Add($"The plan files these facility rates under revenue code {codes}, naming {group.CodeType} {group.Code} in the notes field.");
        }

        if (category.PerUnit)
        {
            notes.Add($"Priced {category.Unit}, not per visit or procedure.");
        }

        if (ServiceCategories.LevelFamily(group.CodeType, group.Code) is { } family)
        {
            notes.Add($"{family}: compare level to level only. How often each level gets billed isn't in these files.");
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
        private readonly Dictionary<long, RateEntry> _rates = [];
        private RateView? _all;
        private RateView? _matched;
        private bool _anyNotEinLevel;

        public string Key { get; } = key;
        public string? Ein { get; } = ein;
        public bool EinUnverified { get; private set; } = einUnverified;
        public bool EinLevel => !_anyNotEinLevel;
        public bool Matched { get; private set; }

        /// <summary>Every rate the provider has in the group.</summary>
        public RateView AllRates => _all ??= RateView.Of(_rates.Values);

        /// <summary>Only the rates that matched the provider filters (every rate when no filter is set, since all match).</summary>
        public RateView MatchedRates => _matched ??= MatchedOrAll();

        /// <summary>One value per provider per group for the statistics (see <see cref="RateView.Value"/>).</summary>
        public decimal Value => AllRates.Value;

        public string DisplayName => _names.Count > 0 ? _names.Min! : "Name not published";

        public IReadOnlyList<string> OtherNames => _names.Skip(1).ToList();

        public string EinDisplay => Ein switch
        {
            null => Key.Replace(":", " ", StringComparison.Ordinal).ToUpperInvariant(),
            _ when EinUnverified => Domain.Ein.Mask(Ein),
            _ => Domain.Ein.Format(Ein),
        };

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
            }
            else
            {
                _rates[fact.RateId] = new RateEntry(fact.RateId, fact.Rate, fact.Occurrences, fact.NpiCount) { Matched = fact.FilterMatch };
            }

            Matched |= fact.FilterMatch;
            EinUnverified |= fact.EinUnverified;
            _anyNotEinLevel |= !fact.EinLevel;
            foreach (var name in (fact.BusinessNames ?? "").Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                _names.Add(name);
            }
        }
    }

    private sealed class RateEntry(long rateId, decimal rate, int occurrences, int npiCount)
    {
        public long RateId { get; } = rateId;
        public decimal Rate { get; } = rate;
        public int Occurrences { get; } = occurrences;
        public int NpiCount { get; } = npiCount;
        public bool Matched { get; set; }
    }

    /// <summary>A provider's rates in one group, summed up for display.</summary>
    /// <param name="Value">
    /// The NPI-weighted median of the rates: always one the provider is really paid (never a midpoint). Most providers
    /// have a single rate per group, so this is usually just that rate.
    /// </param>
    /// <param name="SourceRateId">The rate carrying <paramref name="Value"/> that lists the most NPIs (shown in the source drawer).</param>
    /// <param name="LargestNpiCount">NPIs under the largest single rate: a lower bound until the handler counts them exactly.</param>
    private sealed record RateView(
        decimal Value, decimal Low, decimal High, long SourceRateId, IReadOnlyList<long> RateIds, int Occurrences, int LargestNpiCount)
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
                list.Max(entry => entry.NpiCount));
        }
    }
}

internal static class RateSearchFormat
{
    public static string Money(decimal value) => value.ToString("$#,##0.00", CultureInfo.InvariantCulture);

    public static string SnapshotDate(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "selected";
}
