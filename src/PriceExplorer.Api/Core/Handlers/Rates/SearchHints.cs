using Dapper;
using DuckDB.NET.Data;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Api.Core.Handlers.Rates;

/// <summary>
/// Explains empty or surprising search results in plain words. Each hint's links change the search (a link's values are
/// applied to the current search; null removes that filter), and every link is built so that following it finds rates:
/// it also clears whatever other filter would leave the result empty.
/// </summary>
internal sealed class SearchHints(DuckDBConnection connection, ResolvedSnapshot snapshot, ReferenceCatalog catalog, SearchContext search)
{
    /// <summary>The filters that narrow results beyond the code and the provider.</summary>
    private static readonly (string Key, string? Value)[] ClearNarrowing =
        [("setting", null), ("feeType", null), ("product", null), ("group", null), ("codeType", null)];

    public async Task<List<SearchHint>> ForAsync(List<RateFactRow> facts, AssembledSearch assembled, CodeInfo? code)
    {
        var hints = new List<SearchHint>();
        if (search.Npi is { } npi && !Domain.Npi.IsValid(npi))
        {
            hints.Add(Hint("npi_check_digit", "warning",
                $"NPI {npi} fails the standard NPI check-digit test. Some plan files publish NPIs like this, so the search still ran."));
        }

        var revenue = await RevenueCodeProceduresAsync(directRatesFound: facts.Any(fact => fact.CodeType == BillingCodes.RevenueCode));
        if (facts.Count == 0)
        {
            hints.Add(revenue?.Hint ?? await NotFoundAsync());
            return hints;
        }

        hints.AddRange(CodeShape(facts));
        if (revenue is not null)
        {
            hints.Add(revenue.Hint);
        }

        if (assembled.Groups.Count > 0)
        {
            return hints;
        }

        if (search.CodeType is { } type && facts.All(fact => fact.CodeType != type))
        {
            hints.Add(CodeTypeMismatch(facts.Select(fact => new CodeKind(fact.CodeType, fact.Code)).Distinct().ToList()));
        }
        else if (search.ProviderFilterActive && !facts.Any(fact => fact.FilterMatch))
        {
            // When the provider's rates under a searched revenue code are all filed under procedure codes, the revenue
            // code hint already says where they are; "has no rate" would be misleading.
            if (revenue is not { ProviderScoped: true })
            {
                hints.AddRange(await ProviderAsync(facts, code));
            }
        }
        else
        {
            hints.Add(FiltersHideResults(assembled.AllGroups));
        }

        return hints;
    }

    // ---------- Codes ----------

    /// <summary>Nothing matched: a wrong code type, or a code that isn't in these files.</summary>
    private async Task<SearchHint> NotFoundAsync()
    {
        if (search.CodeType is { } type)
        {
            // Only CPT, HCPCS and revenue codes lose leading zeros in the files; any other type (a DRG, say) is looked up
            // exactly as typed, so DRG 470 doesn't "match" revenue code 0470.
            var lookFor = type is BillingCodes.Cpt or BillingCodes.Hcpcs or BillingCodes.RevenueCode
                ? CodeInput.Candidates(search.Code, null)
                : [search.Code];
            var kinds = (await connection.QueryAsync<CodeKind>(
                    SqlFile.For<SearchHints>("CodeTypesFor.sql"), new { snapshotId = snapshot.SnapshotId, codes = lookFor }))
                .Where(kind => kind.CodeType != type)
                .ToList();
            if (kinds.Count > 0)
            {
                return CodeTypeMismatch(kinds);
            }
        }

        return await CodeNotFoundAsync();
    }

    private async Task<SearchHint> CodeNotFoundAsync()
    {
        if (search.CodeType is { } type && type.Contains("DRG", StringComparison.Ordinal))
        {
            return Hint("code_not_found", "info",
                $"{type} {search.Code} is an inpatient DRG. These files list outpatient, fee-for-service rates only, so inpatient stays won't appear.");
        }

        var longest = search.Candidates.OrderByDescending(candidate => candidate.Length).First();
        var prefix = longest[..Math.Min(3, longest.Length)];
        var similar = (await connection.QueryAsync<SimilarCodeRow>(
            SqlFile.For<SearchHints>("SimilarCodes.sql"), new { snapshotId = snapshot.SnapshotId, prefix })).ToList();

        var message = $"No rates for {search.Code} in the {RateSearchFormat.SnapshotDate(snapshot.IndexDate)} snapshot. " +
            "These files list outpatient, fee-for-service rates only, so inpatient stays (billed by DRG) and many specialty codes won't appear.";
        if (similar.Count > 0)
        {
            message += " Codes that start the same way:";
        }

        return Hint("code_not_found", "info", message, similar
            .Select(row => Link($"{row.CodeType} {row.Code}{(row.Name is null ? "" : $" · {row.Name}")}", [("code", row.Code), ("codeType", row.CodeType), .. ClearNarrowing]))
            .ToList());
    }

    private SearchHint CodeTypeMismatch(IReadOnlyList<CodeKind> kinds) =>
        Hint("code_type_mismatch", "info",
            $"No {search.CodeType} rates for {search.Code}. It's in these files as {string.Join(" and ", kinds.Select(kind => $"{kind.CodeType} {kind.Code}"))}.",
            kinds.Select(kind => Link($"Search {kind.CodeType} {kind.Code}", [("code", kind.Code), ("codeType", kind.CodeType), .. ClearNarrowing])).ToList());

    private IEnumerable<SearchHint> CodeShape(List<RateFactRow> facts)
    {
        var kinds = facts.Select(fact => (fact.CodeType, fact.Code)).Distinct().OrderBy(kind => kind.CodeType, StringComparer.Ordinal).ToList();
        if (kinds.Count > 1)
        {
            yield return Hint("ambiguous_code", "info",
                $"\"{search.Code}\" matches more than one kind of code. Each shows as its own group; pick one to narrow the results.",
                kinds.Select(kind => Link($"Only {kind.CodeType} {kind.Code}", ("code", kind.Code), ("codeType", kind.CodeType))).ToList());
        }

        foreach (var kind in kinds)
        {
            var rows = facts.Where(fact => fact.CodeType == kind.CodeType && fact.Code == kind.Code).ToList();
            if (kind.CodeType != BillingCodes.RevenueCode && rows.All(fact => fact.CodeSource == "additional_information"))
            {
                var revenueCodes = string.Join(", ", rows.Select(fact => fact.BillingCode).Distinct().Order()
                    .Select(rc => $"{rc} ({catalog.RevenueCodeLabel(rc)})"));
                yield return Hint("code_facility_only", "info",
                    $"{kind.CodeType} {kind.Code} isn't published as its own billing code in these files. It only appears as facility rates the plan files under revenue code {revenueCodes}, with the procedure code in a notes field.");
            }
        }
    }

    /// <summary>
    /// Facility rates under a revenue code are usually filed under the procedure code in their notes field, so searching the
    /// revenue code misses them. Points to the procedure codes, for the filtered provider when it has such rates.
    /// </summary>
    private async Task<RevenueHint?> RevenueCodeProceduresAsync(bool directRatesFound)
    {
        // Every revenue code the input can mean, not only those with direct rates: a revenue code whose rates are all
        // filed under procedure codes never shows up in the search itself (RC 0731 behind CPT 00731).
        if (search.CodeType is not (null or BillingCodes.RevenueCode))
        {
            return null;
        }

        var revenueCodes = search.Candidates.Where(candidate => candidate.Length == 4 && candidate.All(char.IsAsciiDigit)).ToList();
        if (revenueCodes.Count == 0)
        {
            return null;
        }

        var scoped = search.ProviderFilterActive ? await ProceduresAsync(revenueCodes, forProvider: true) : [];
        var rows = scoped.Count > 0 ? scoped : await ProceduresAsync(revenueCodes, forProvider: false);
        if (rows.Count == 0)
        {
            return null;
        }

        var providerScoped = scoped.Count > 0;
        var label = string.Join(", ", rows.Select(row => row.RevenueCode).Distinct().Order()
            .Select(rc => $"revenue code {rc} ({catalog.RevenueCodeLabel(rc)})"));
        var total = rows.Sum(row => row.Rates);
        var codes = rows.Select(row => (row.CodeType, row.Code)).Distinct().Count();
        var message = providerScoped
            ? $"This provider's {Plural(total, "facility rate")} under {label} {(total == 1 ? "is" : "are")} filed under the procedure code " +
              $"in the notes field ({Plural(codes, "code")}). Search one to see {(total == 1 ? "it" : "them")}:"
            : directRatesFound
                ? $"Another {Plural(total, "facility rate")} under {label} {(total == 1 ? "is" : "are")} filed under the procedure code in the notes field " +
                  $"({Plural(codes, "code")}), so they appear when you search that procedure code."
                : $"All {Plural(total, "facility rate")} under {label} {(total == 1 ? "is" : "are")} filed under the procedure code in the notes field " +
                  $"({Plural(codes, "code")}). Search a procedure code to compare like for like.";
        if (!providerScoped)
        {
            message += search.ProviderFilterActive ? " None of them are the provider you searched for. Most common:" : " Most common:";
        }

        // The links keep the provider filters only when that provider has rates behind them.
        (string Key, string? Value)[] clear = providerScoped
            ? ClearNarrowing
            : [.. ClearNarrowing, ("npi", null), ("ein", null), ("provider", null)];
        var links = rows
            .GroupBy(row => (row.CodeType, row.Code))
            .Select(group => (group.Key.CodeType, group.Key.Code, Rates: group.Sum(row => row.Rates)))
            .OrderByDescending(row => row.Rates)
            .ThenBy(row => row.Code, StringComparer.Ordinal)
            .Take(5)
            .Select(row => Link($"{row.CodeType} {row.Code} · {Plural(row.Rates, "rate")}", [("code", row.Code), ("codeType", row.CodeType), .. clear]))
            .ToList();
        return new RevenueHint(Hint("revenue_code_procedures", "info", message, links), providerScoped);
    }

    private async Task<List<RevenueProcedureRow>> ProceduresAsync(IReadOnlyList<string> revenueCodes, bool forProvider) =>
        (await connection.QueryAsync<RevenueProcedureRow>(
            SqlFile.For<SearchHints>("RevenueCodeProcedures.sql"),
            new
            {
                snapshotId = snapshot.SnapshotId,
                codes = revenueCodes,
                product = search.Product,
                npi = forProvider ? search.Npi : null,
                ein = forProvider ? search.Ein : null,
                provider = forProvider ? search.Name : null,
            })).ToList();

    // ---------- Providers ----------

    /// <summary>
    /// The provider filters matched no rate for this code. Says which filter is the reason: the NPI/EIN has no rate for
    /// the code, no provider name matches, or each matches on its own but never in the same provider group.
    /// </summary>
    private async Task<List<SearchHint>> ProviderAsync(List<RateFactRow> facts, CodeInfo? code)
    {
        var hints = new List<SearchHint>();
        var codeLabel = code is null ? "this code" : $"{code.CodeType} {code.Code}";
        var idProblem = search.IdFilter && !facts.Any(fact => fact.IdMatch);
        var nameProblem = search.NameTyped is not null && !facts.Any(fact => fact.NameMatch);

        if (idProblem)
        {
            hints.AddRange(await IdHintsAsync(facts, codeLabel));
        }

        if (nameProblem)
        {
            var message = $"No provider whose name contains \"{search.NameTyped}\" has a rate for {codeLabel}.";
            hints.Add(idProblem
                // The NPI/EIN fails too, so clearing only the name would still find nothing.
                ? Hint("provider_name_no_match", "info", message,
                    [Link($"Search {codeLabel} for all providers", [("provider", null), ("npi", null), ("ein", null), .. ClearNarrowing])])
                : Hint("provider_name_no_match", "info", message, [ClearNameLink(facts)]));
        }

        if (!idProblem && !nameProblem && search.IdFilter && search.NameTyped is not null)
        {
            // Both match on their own, but never in the same provider group.
            hints.Add(Hint("provider_name_no_match", "info",
                $"{IdLabel()} has rates for {codeLabel}, but not under a name containing \"{search.NameTyped}\".",
                [ClearNameLink(facts)]));
        }

        return hints;
    }

    private async Task<List<SearchHint>> IdHintsAsync(List<RateFactRow> facts, string codeLabel)
    {
        var (npi, ein) = (search.Npi, search.Ein);

        // The NPI has rates for this code, but under a different tax ID than the one entered.
        if (npi is not null && ein is not null && facts.Any(fact => fact.NpiMatch))
        {
            return [Hint("npi_other_ein", "info",
                $"NPI {npi} has no rate for {codeLabel} under {EinLabel(ein)}, but has {codeLabel} rates under " +
                $"{DescribeProviders(facts.Where(fact => fact.NpiMatch))}. Contracts are signed per tax ID, so the NPI and EIN must match.",
                [Link("Search this NPI without the EIN", [("ein", null), ("provider", null), .. ClearNarrowing])])];
        }

        var both = await LookupAsync(npi, ein);
        if (both.GroupCount == 0 && npi is not null && ein is not null)
        {
            var npiOnly = await LookupAsync(npi, null);
            return npiOnly.GroupCount == 0
                ? [Hint("npi_not_listed", "info", $"NPI {npi} isn't listed in this snapshot's files.")]
                : [Hint("npi_other_ein", "info",
                    $"NPI {npi} is listed under a different tax ID: {npiOnly.Describe()}. It has no rate for {codeLabel} there either" +
                    $"{OtherCodes(npiOnly)}. Contracts are signed per tax ID, so the NPI and EIN must match.",
                    npiOnly.TopCodeList.Select(top => Link($"Search {top} for this NPI", [("code", top), ("ein", null), ("provider", null), .. ClearNarrowing])).ToList())];
        }

        if (both.GroupCount == 0)
        {
            return [npi is not null
                ? Hint("npi_not_listed", "info", $"NPI {npi} isn't listed in this snapshot's files.")
                : Hint("ein_not_listed", "info", $"{(ein!.StartsWith('u') ? "This provider" : $"EIN {Ein.Format(ein)}")} isn't listed in this snapshot's files.")];
        }

        // Not listed on any rate for this code, but its tax ID holds a contract for the code that covers the whole tax ID.
        if (npi is not null && both.WholeTinKeyList.Count > 0)
        {
            return [Hint("provider_whole_tin", "info",
                $"NPI {npi} isn't listed on any {codeLabel} rate, but {both.WholeTinLabel} has a {codeLabel} contract that covers the " +
                "whole tax ID (the file lists no NPIs for it), which may include this clinician.",
                [Link("See that tax ID's rate", [("npi", null), ("ein", both.WholeTinKeyList[0]), ("provider", null), .. ClearNarrowing])])];
        }

        var who = npi is not null ? $"NPI {npi} ({both.Describe()})" : both.Describe();
        var links = new List<HintLink>();
        if (npi is not null && ein is not null && facts.Any(fact => fact.EinMatch))
        {
            links.Add(Link("Search this EIN without the NPI", [("npi", null), ("provider", null), .. ClearNarrowing]));
        }

        links.AddRange(both.TopCodeList.Select(top => Link($"Search {top}", [("code", top), ("provider", null), .. ClearNarrowing])));
        return [Hint("provider_no_rate", "info", $"{who} has no rate for {codeLabel} in these files{OtherCodes(both)}.", links)];
    }

    /// <summary>"Clear the name filter", also clearing the narrowing filters when they would still leave nothing.</summary>
    private HintLink ClearNameLink(List<RateFactRow> facts) =>
        facts.Any(fact => (!search.IdFilter || fact.IdMatch) && SurvivesNarrowing(fact))
            ? Link("Clear the name filter", ("provider", null))
            : Link("Clear the name filter", [("provider", null), .. ClearNarrowing]);

    private bool SurvivesNarrowing(RateFactRow fact) =>
        (search.Setting is null || catalog.SettingForPlace(fact.Pos).Key == search.Setting)
        && (search.FeeType is null || RateSearchAssembler.FeeTypeOf(fact.BillingClass) == search.FeeType)
        && (search.Product is null || fact.ProductKey == search.Product)
        && (search.CodeType is null || fact.CodeType == search.CodeType);

    /// <summary>The code has rates, but the setting / fee type / product / code type filters hide them all.</summary>
    private SearchHint FiltersHideResults(IReadOnlyList<RateGroup> allGroups)
    {
        var applied = new List<string>();
        if (search.Setting is { } setting) applied.Add(catalog.Setting(setting)?.Label ?? setting);
        if (search.FeeType is { } feeType) applied.Add(feeType == "facility" ? "facility fee" : "professional fee");
        if (search.Product is { } product) applied.Add(product);
        if (search.CodeType is { } codeType) applied.Add(codeType);

        // Only settings that really have rates for this search: with a provider filter, that provider's groups (a group
        // lists the filtered provider among its highlights). Each link clears the other narrowing filters.
        var settings = allGroups
            .Where(group => !search.ProviderFilterActive || group.Highlights.Count > 0)
            .Select(group => (group.SettingKey, group.SettingLabel))
            .Distinct()
            .Select(option => Link(option.SettingLabel, [("setting", option.SettingKey), .. ClearNarrowing]))
            .ToList();

        return Hint("filters_hide_results", "info",
            $"This code has rates{(search.ProviderFilterActive ? " for this provider" : "")}, but none match the filters ({string.Join(", ", applied)}). Settings with rates:",
            settings);
    }

    // ---------- Helpers ----------

    private async Task<ProviderLookupRow> LookupAsync(long? npi, string? ein) =>
        await connection.QuerySingleAsync<ProviderLookupRow>(
            SqlFile.For<SearchHints>("ProviderLookup.sql"), new { snapshotId = snapshot.SnapshotId, codes = search.Candidates, npi, ein });

    private string IdLabel() =>
        search.Npi is not null ? $"NPI {search.Npi}" : search.Ein is { } ein ? EinLabel(ein) : "This provider";

    private static string EinLabel(string ein) => ein.StartsWith('u') ? "this tax ID" : $"EIN {Ein.Format(ein)}";

    private static string OtherCodes(ProviderLookupRow row) =>
        row.CodeCount == 0 ? "" : $". It has rates for {Plural(row.CodeCount, "other code")}";

    /// <summary>"Teladoc Physicians PC, EIN 45-4115569", or the first one "and N other tax IDs".</summary>
    private static string DescribeProviders(IEnumerable<RateFactRow> facts)
    {
        var providers = facts
            .GroupBy(fact => fact.ProviderKey, StringComparer.Ordinal)
            .Select(group => (
                Name: group.SelectMany(fact => (fact.BusinessNames ?? "").Split(" | ", StringSplitOptions.RemoveEmptyEntries))
                    .Order(StringComparer.Ordinal).FirstOrDefault() ?? "a provider",
                group.First().Ein,
                Masked: group.Any(fact => fact.EinUnverified)))
            .OrderBy(provider => provider.Name, StringComparer.Ordinal)
            .ToList();

        static string Label((string Name, string? Ein, bool Masked) provider) =>
            provider.Ein is null ? provider.Name
            : provider.Masked ? $"{provider.Name} (masked tax ID)"
            : $"{provider.Name}, EIN {Ein.Format(provider.Ein)}";

        return providers.Count switch
        {
            1 => Label(providers[0]),
            2 => $"{Label(providers[0])} and {Label(providers[1])}",
            var count => $"{Label(providers[0])} and {count - 1} other tax IDs",
        };
    }

    private static string Plural(long count, string noun) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{count:N0} {noun}{(count == 1 ? "" : "s")}");

    private static SearchHint Hint(string kind, string severity, string message, IReadOnlyList<HintLink>? links = null) =>
        new(kind, severity, message, links ?? []);

    /// <summary>A link that changes the search. When a key repeats, the first value wins (specific values come first).</summary>
    private static HintLink Link(string label, params (string Key, string? Value)[] query)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in query)
        {
            values.TryAdd(key, value);
        }

        return new HintLink(label, values);
    }

    private sealed record RevenueHint(SearchHint Hint, bool ProviderScoped);

    private sealed record CodeKind(string CodeType, string Code);

    private sealed record SimilarCodeRow(string CodeType, string Code, long Rates, string? Name);

    private sealed record RevenueProcedureRow(string RevenueCode, string CodeType, string Code, long Rates);

    private sealed record ProviderLookupRow(
        long GroupCount, string? Eins, bool? AnyUnverified, string? BusinessNames, long CodeCount, string? TopCodes,
        string? WholeTinKeys, string? WholeTinNames)
    {
        public IReadOnlyList<string> TopCodeList => (TopCodes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);

        public IReadOnlyList<string> WholeTinKeyList => (WholeTinKeys ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);

        /// <summary>"Church Mission of Help of Western New York Inc, EIN 16-0743964".</summary>
        public string WholeTinLabel
        {
            get
            {
                var name = (WholeTinNames ?? "").Split(" | ", StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "its tax ID";
                var key = WholeTinKeyList.FirstOrDefault();
                return key is null ? name : key.StartsWith('u') ? $"{name} (masked tax ID)" : $"{name}, EIN {Ein.Format(key)}";
            }
        }

        public string Describe()
        {
            var names = (BusinessNames ?? "").Split(" | ", StringSplitOptions.RemoveEmptyEntries);
            var name = names.Length switch
            {
                0 => "a provider",
                1 => names[0],
                _ => $"{names[0]} and {names.Length - 1} other name{(names.Length == 2 ? "" : "s")}",
            };
            var eins = (Eins ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Ein.Format).ToList();
            var ids = eins.Count > 0 ? $"EIN {string.Join(", ", eins)}" : AnyUnverified == true ? "a masked tax ID" : null;
            return ids is null ? name : $"{name}, {ids}";
        }
    }
}
