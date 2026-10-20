using System.Globalization;
using Dapper;
using DuckDB.NET.Data;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Api.Core.Handlers.Rates;

/// <summary>
/// Explains empty or surprising search results in plain words. Each hint's links change the search (a link's values are
/// applied to the current search; null removes that filter). Links are built to find rates, clearing whatever other
/// filter would leave the result empty, and <see cref="HintLinkChecker"/> then drops any link that still finds none.
/// </summary>
internal sealed class SearchHints(DuckDBConnection connection, ResolvedSnapshot snapshot, ReferenceCatalog catalog, SearchContext search)
{
    /// <summary>The filters that narrow results beyond the code and the provider.</summary>
    private static readonly (string Key, string? Value)[] ClearNarrowing =
        [("setting", null), ("feeType", null), ("product", null), ("group", null), ("codeType", null)];

    private static readonly (string Key, string? Value)[] ClearProvider = [("npi", null), ("ein", null), ("provider", null)];

    public async Task<List<SearchHint>> ForAsync(List<RateFactRow> facts, AssembledSearch assembled, CodeInfo? code)
    {
        var hints = new List<SearchHint>();
        if (search.Npi is { } npi && !Domain.Npi.IsValid(npi))
        {
            hints.Add(NpiCheck(npi));
        }

        var revenue = await RevenueCodeProceduresAsync(
            directRatesFound: facts.Any(fact => fact.CodeType == BillingCodes.RevenueCode), RevenueCodeCandidates());
        if (facts.Count == 0)
        {
            hints.AddRange(revenue is not null ? [revenue.Hint] : await NotFoundAsync());
            return hints;
        }

        hints.AddRange(CodeShape(facts, assembled));
        if (revenue is not null)
        {
            hints.Add(revenue.Hint);
        }

        if (assembled.Groups.Count > 0)
        {
            // Groups show, but the selected comparison group isn't one of them (a stale link, or another provider's group).
            if (search.Group is { } group && assembled.Groups.All(shown => shown.Key != group))
            {
                hints.Add(Hint("group_not_in_results", "info",
                    $"The comparison group you picked has no rates {(search.ProviderFilterActive ? $"for {FilteredProviders} " : "")}" +
                    $"under these filters. {Plural(assembled.Groups.Count, "other group")} {(assembled.Groups.Count == 1 ? "does" : "do")}.",
                    [Link("Show all groups", ("group", null))]));
            }

            return hints;
        }

        // Nothing shows. Say why: the code type, the provider, or the other filters.
        var typeMismatch = search.CodeType is { } type && facts.All(fact => fact.CodeType != type);
        if (typeMismatch)
        {
            hints.Add(CodeTypeMismatch(facts
                .GroupBy(fact => new CodeKind(fact.CodeType, fact.Code))
                .Select(kind => new KindOption(kind.Key, ForProvider: !search.ProviderFilterActive || kind.Any(fact => fact.FilterMatch)))
                .OrderBy(option => option.Kind.CodeType, StringComparer.Ordinal)
                .ToList()));
        }

        if (search.ProviderFilterActive && !facts.Any(fact => fact.FilterMatch))
        {
            // When the provider's rates under a searched revenue code are filed under procedure codes, the revenue code
            // hint already says where they are; "has no rate" would be wrong.
            if (revenue is not { ProviderScoped: true })
            {
                hints.AddRange(await ProviderAsync(facts, code));
            }
        }
        else if (!typeMismatch)
        {
            hints.Add(FiltersHideResults(assembled.AllGroups));
        }

        return hints;
    }

    // ---------- Codes ----------

    /// <summary>No rate is stored under the code at all: it's filed under another code type, or it isn't in these files.</summary>
    private async Task<List<SearchHint>> NotFoundAsync()
    {
        // A type with its own numbering (a DRG, an APC) never matches these files' codes, whatever the digits.
        if (search.CodeType is not { } type || search.ForeignCodeType)
        {
            return [await CodeNotFoundAsync()];
        }

        // Every stored form the digits can take: "CPT 0490" is revenue code 0490.
        var kinds = (await connection.QueryAsync<CodeKindRow>(
                SqlFile.For<SearchHints>("CodeTypesFor.sql"),
                new
                {
                    snapshotId = snapshot.SnapshotId,
                    codes = CodeInput.Candidates(search.Code, null),
                    npi = search.Npi,
                    ein = search.Ein,
                    provider = search.Name,
                }))
            .Where(kind => kind.CodeType != type)
            .ToList();

        var hints = new List<SearchHint>();
        var searchable = kinds
            .Where(kind => kind.Searchable)
            .Select(kind => new KindOption(new CodeKind(kind.CodeType, kind.Code), kind.ForProvider))
            .ToList();
        if (searchable.Count > 0)
        {
            hints.Add(CodeTypeMismatch(searchable));
        }

        // Facility rates filed under a revenue code with these digits are found under their procedure codes.
        var revenueCodes = kinds.Where(kind => kind.CodeType == BillingCodes.RevenueCode).Select(kind => kind.Code).ToList();
        var revenue = await RevenueCodeProceduresAsync(
            directRatesFound: searchable.Any(option => option.Kind.CodeType == BillingCodes.RevenueCode),
            revenueCodes,
            lead: searchable.Count == 0 ? $"No {type} rates for {search.Code}." : null);
        if (revenue is not null)
        {
            hints.Add(revenue.Hint);
        }

        return hints.Count > 0 ? hints : [await CodeNotFoundAsync()];
    }

    private async Task<SearchHint> CodeNotFoundAsync()
    {
        if (search.CodeType is { } type && type.Contains("DRG", StringComparison.Ordinal))
        {
            return Hint("code_not_found", "info",
                $"{type} {search.Code} is an inpatient DRG. These files list outpatient, fee-for-service rates only, so inpatient stays won't appear.");
        }

        if (search.ForeignCodeType)
        {
            return Hint("code_not_found", "info",
                $"No {search.CodeType} rates for {search.Code}. These files list rates by CPT, HCPCS and revenue code only.");
        }

        // Codes the filtered provider has first; otherwise the busiest codes for everyone, dropping the provider filter.
        var similar = search.ProviderFilterActive ? await SimilarCodesAsync(forProvider: true) : [];
        var providerScoped = similar.Count > 0;
        if (!providerScoped)
        {
            similar = await SimilarCodesAsync(forProvider: false);
        }

        var message = $"No {(search.CodeType is { } typed ? $"{typed} " : "")}rates for {search.Code} in the " +
            $"{RateSearchFormat.SnapshotDate(snapshot.IndexDate)} snapshot. These files list outpatient, fee-for-service rates only, " +
            "so inpatient stays (billed by DRG) and many specialty codes won't appear.";
        if (similar.Count > 0)
        {
            message += providerScoped ? " Codes this provider has that start the same way:"
                : search.ProviderFilterActive ? " The provider you searched for has no codes that start the same way. For all providers:"
                : " Codes that start the same way:";
        }

        (string Key, string? Value)[] clear = providerScoped || !search.ProviderFilterActive ? ClearNarrowing : [.. ClearNarrowing, .. ClearProvider];
        return Hint("code_not_found", "info", message, similar
            .Select(row => Link($"{row.CodeType} {row.Code}{(row.Name is null ? "" : $" · {row.Name}")}", [("code", row.Code), ("codeType", row.CodeType), .. clear]))
            .ToList());
    }

    /// <summary>
    /// Codes that start like the input, trying the code as typed before its zero-padded forms ("4537" suggests 4537x
    /// colonoscopy codes, not revenue codes 045x). A revenue code search only tries the revenue code.
    /// </summary>
    private async Task<List<SimilarCodeRow>> SimilarCodesAsync(bool forProvider)
    {
        var forms = search.CodeType == BillingCodes.RevenueCode ? search.Candidates : search.Candidates.Prepend(search.Code);
        foreach (var prefix in forms.OrderBy(form => form.Length).Select(form => form[..Math.Min(3, form.Length)]).Distinct(StringComparer.Ordinal))
        {
            var rows = (await connection.QueryAsync<SimilarCodeRow>(
                SqlFile.For<SearchHints>("SimilarCodes.sql"),
                new
                {
                    snapshotId = snapshot.SnapshotId,
                    prefix,
                    npi = forProvider ? search.Npi : null,
                    ein = forProvider ? search.Ein : null,
                    provider = forProvider ? search.Name : null,
                })).ToList();
            if (rows.Count > 0)
            {
                return rows;
            }
        }

        return [];
    }

    /// <summary>The code is in these files as other kinds; each link searches one, for all providers when this one lacks it.</summary>
    private SearchHint CodeTypeMismatch(IReadOnlyList<KindOption> options) =>
        Hint("code_type_mismatch", "info",
            $"No {search.CodeType} rates for {search.Code}. It's in these files as {string.Join(" and ", options.Select(option => option.Kind.Label))}.",
            options.Select(option => option.ForProvider
                    ? Link($"Search {option.Kind.Label}", [("code", option.Kind.Code), ("codeType", option.Kind.CodeType), .. ClearNarrowing])
                    : Link($"Search {option.Kind.Label} for all providers",
                        [("code", option.Kind.Code), ("codeType", option.Kind.CodeType), .. ClearNarrowing, .. ClearProvider]))
                .ToList());

    private IEnumerable<SearchHint> CodeShape(List<RateFactRow> facts, AssembledSearch assembled)
    {
        // Kinds shown under the current filters (with a provider filter, only kinds where that provider has rates).
        var shown = assembled.Groups.Select(group => new CodeKind(group.CodeType, group.Code)).Distinct()
            .OrderBy(kind => kind.CodeType, StringComparer.Ordinal).ToList();
        var all = facts.Select(fact => new CodeKind(fact.CodeType, fact.Code)).Distinct()
            .OrderBy(kind => kind.CodeType, StringComparer.Ordinal).ToList();

        if (shown.Count > 1)
        {
            yield return Hint("ambiguous_code", "info",
                $"\"{search.Code}\" matches more than one kind of code. Each shows as its own group; pick one to narrow the results.",
                shown.Select(kind => Link($"Only {kind.Label}", [("code", kind.Code), ("codeType", kind.CodeType), .. ClearGroupUnlessOf(kind)])).ToList());
        }
        else if (shown.Count == 1 && search.CodeType is null && all.Except(shown).ToList() is { Count: > 0 } hidden)
        {
            // The number also matches another kind of code whose rates the current filters hide.
            yield return Hint("ambiguous_code", "info",
                $"\"{search.Code}\" also matches {string.Join(" and ", hidden.Select(kind => kind.Label))}, hidden by the current filters.",
                hidden.Select(kind => ProviderHas(facts, kind)
                        ? Link($"Search {kind.Label}", [("code", kind.Code), ("codeType", kind.CodeType), .. ClearNarrowing])
                        : Link($"Search {kind.Label} for all providers", [("code", kind.Code), ("codeType", kind.CodeType), .. ClearNarrowing, .. ClearProvider]))
                    .ToList());
        }

        foreach (var kind in all)
        {
            var rows = facts.Where(fact => fact.CodeType == kind.CodeType && fact.Code == kind.Code).ToList();
            if (kind.CodeType != BillingCodes.RevenueCode && rows.All(fact => fact.CodeSource == "additional_information"))
            {
                var revenueCodes = rows.Select(fact => fact.BillingCode).Distinct().Order().ToList();
                yield return Hint("code_facility_only", "info",
                    $"{kind.Label} isn't published as its own billing code in these files. It only appears as facility rates the plan " +
                    $"files under {RevenueCodeLabel(revenueCodes)}, with the procedure code in a notes field.");
            }
        }
    }

    /// <summary>A link to one kind of code keeps the selected group only when it's a group of that kind.</summary>
    private (string Key, string? Value)[] ClearGroupUnlessOf(CodeKind kind) =>
        search.Group?.Split('|') is [_, var codeType, var code, ..] && codeType == kind.CodeType && code == kind.Code ? [] : [("group", null)];

    private bool ProviderHas(List<RateFactRow> facts, CodeKind kind) =>
        !search.ProviderFilterActive || facts.Any(fact => fact.CodeType == kind.CodeType && fact.Code == kind.Code && fact.FilterMatch);

    // ---------- Revenue codes ----------

    /// <summary>Every revenue code the input can mean ("490" → 0490), unless another code type was asked for.</summary>
    private IReadOnlyList<string> RevenueCodeCandidates() =>
        search.CodeType is null or BillingCodes.RevenueCode
            ? search.Candidates.Where(candidate => candidate.Length == 4 && candidate.All(char.IsAsciiDigit)).ToList()
            : [];

    /// <summary>
    /// Facility rates under a revenue code are usually filed under the procedure code in their notes field, so searching
    /// the revenue code misses them. Points to the procedure codes: the filtered provider's own when it has such rates
    /// (saying which filter hides them: the product, the name, or an EIN the NPI isn't listed under), otherwise the
    /// market's. Includes revenue codes whose rates are all filed this way (RC 0731 behind CPT 93000).
    /// </summary>
    private async Task<RevenueHint?> RevenueCodeProceduresAsync(bool directRatesFound, IReadOnlyList<string> revenueCodes, string? lead = null)
    {
        if (revenueCodes.Count == 0)
        {
            return null;
        }

        foreach (var scope in ProviderScopes())
        {
            var scoped = await ProceduresAsync(revenueCodes, scope);
            if (scoped.Count > 0)
            {
                return new RevenueHint(ProviderRevenueHint(scoped, scope, lead), ProviderScoped: true);
            }
        }

        var rows = await ProceduresAsync(revenueCodes, new RevenueScope(null, null, null, search.Product));
        return rows.Count == 0 ? null : new RevenueHint(MarketRevenueHint(rows, directRatesFound, lead), ProviderScoped: false);
    }

    /// <summary>
    /// The provider filters as searched, then relaxed one at a time: any product, then any name (when an NPI or EIN
    /// names the provider), then any tax ID for the NPI. The first scope with rates explains where they are.
    /// </summary>
    private IEnumerable<RevenueScope> ProviderScopes()
    {
        if (!search.ProviderFilterActive)
        {
            yield break;
        }

        yield return new RevenueScope(search.Npi, search.Ein, search.Name, search.Product);
        if (search.Product is not null)
        {
            yield return new RevenueScope(search.Npi, search.Ein, search.Name, null) { OtherProduct = true };
        }

        if (search.IdFilter && search.Name is not null)
        {
            yield return new RevenueScope(search.Npi, search.Ein, null, null) { OtherProduct = search.Product is not null, OtherName = true };
        }

        if (search.Npi is not null && search.Ein is not null)
        {
            yield return new RevenueScope(search.Npi, null, null, null)
            {
                OtherProduct = search.Product is not null, OtherName = search.Name is not null, OtherEin = true,
            };
        }
    }

    private SearchHint ProviderRevenueHint(List<RevenueProcedureRow> rows, RevenueScope scope, string? lead)
    {
        var label = RevenueCodeLabel(rows.Select(row => row.RevenueCode).Distinct().Order().ToList());
        var total = rows.Sum(row => row.Rates);
        var codes = rows.Select(row => (row.CodeType, row.Code)).Distinct().Count();
        var first = rows[0];
        string message;
        if (!scope.Relaxed)
        {
            var whose = search.IdFilter
                ? $"for {IdLabel()}"
                : $"for providers whose name contains \"{search.NameTyped}\" ({Plural(first.Providers, "tax ID")})";
            message = total == 1
                ? $"The only facility rate under {label} {whose} is filed under the procedure code in its notes field ({first.CodeType} {first.Code}). Search it:"
                : $"The {Plural(total, "facility rate")} under {label} {whose} are filed under the procedure code in the notes field " +
                  $"({Plural(codes, "code")}). Search one to see them:";
        }
        else
        {
            // Relaxing a filter found them: say where they are, and which filter hides them.
            var subject = search.IdFilter ? IdLabel() : $"Providers whose name contains \"{search.NameTyped}\"";
            var has = search.IdFilter ? "has" : "have";
            message = $"{subject} {has} {Plural(total, "facility rate")} under {label}";
            if (scope.OtherEin)
            {
                message += $", listed under {Taxpayers(first)}, not under {EinLabel(search.Ein!)}";
            }
            else if (scope.OtherProduct && !scope.OtherName)
            {
                message += $", {(total == 1 ? "in" : "all in")} {first.Products}";
            }

            if (scope.OtherName)
            {
                message += $", but none under a name containing \"{search.NameTyped}\"";
            }

            message += total == 1
                ? $". It is filed under the procedure code in its notes field ({first.CodeType} {first.Code})."
                : $". They're filed under the procedure code in the notes field ({Plural(codes, "code")}).";
            if (scope.OtherEin)
            {
                message += " Contracts are signed per tax ID, so the NPI and EIN must match.";
            }

            message += total == 1 ? " Search it:" : " Search one to see them:";
        }

        // The links keep the provider, minus the filters that hid these rates.
        var clear = new List<(string Key, string? Value)>(ClearNarrowing);
        if (scope.OtherName || scope.OtherEin)
        {
            clear.Add(("provider", null));
        }

        if (scope.OtherEin)
        {
            clear.Add(("ein", null));
        }

        return Hint("revenue_code_procedures", "info", Lead(lead, message), ProcedureLinks(rows, [.. clear]));
    }

    private SearchHint MarketRevenueHint(List<RevenueProcedureRow> rows, bool directRatesFound, string? lead)
    {
        var label = RevenueCodeLabel(rows.Select(row => row.RevenueCode).Distinct().Order().ToList());
        var total = rows.Sum(row => row.Rates);
        var codes = rows.Select(row => (row.CodeType, row.Code)).Distinct().Count();
        var only = $"{rows[0].CodeType} {rows[0].Code}";
        var message = (directRatesFound, total == 1) switch
        {
            (true, true) => $"One more facility rate under {label} is filed under the procedure code in its notes field ({only}), so it appears when you search that code.",
            (true, false) => $"Another {Plural(total, "facility rate")} under {label} are filed under the procedure code in the notes field " +
                             $"({Plural(codes, "code")}), so they appear when you search those procedure codes.",
            (false, true) => $"The only facility rate under {label} is filed under the procedure code in its notes field ({only}). Search that code to compare like for like.",
            (false, false) => $"All {Plural(total, "facility rate")} under {label} are filed under the procedure code in the notes field " +
                              $"({Plural(codes, "code")}). Search a procedure code to compare like for like.",
        };
        if (search.ProviderFilterActive)
        {
            message += total == 1 ? " It isn't from the provider you searched for." : " None of them are from the provider you searched for.";
        }

        if (total > 1)
        {
            message += " Most common:";
        }

        // These rates aren't the filtered provider's, so the links search them for everyone.
        return Hint("revenue_code_procedures", "info", Lead(lead, message), ProcedureLinks(rows, [.. ClearNarrowing, .. ClearProvider]));
    }

    private static List<HintLink> ProcedureLinks(List<RevenueProcedureRow> rows, (string Key, string? Value)[] clear) =>
        rows
            .GroupBy(row => (row.CodeType, row.Code))
            .Select(group => (group.Key.CodeType, group.Key.Code, Rates: group.Sum(row => row.Rates)))
            .OrderByDescending(row => row.Rates)
            .ThenBy(row => row.Code, StringComparer.Ordinal)
            .Take(5)
            .Select(row => Link($"{row.CodeType} {row.Code} · {Plural(row.Rates, "rate")}", [("code", row.Code), ("codeType", row.CodeType), .. clear]))
            .ToList();

    private async Task<List<RevenueProcedureRow>> ProceduresAsync(IReadOnlyList<string> revenueCodes, RevenueScope scope) =>
        (await connection.QueryAsync<RevenueProcedureRow>(
            SqlFile.For<SearchHints>("RevenueCodeProcedures.sql"),
            new { snapshotId = snapshot.SnapshotId, codes = revenueCodes, product = scope.Product, npi = scope.Npi, ein = scope.Ein, provider = scope.Name }))
        .ToList();

    /// <summary>"revenue code 0490 (Ambulatory surgery)", or "revenue codes 0490 (…), 0491 (…)".</summary>
    private string RevenueCodeLabel(IReadOnlyList<string> codes) =>
        $"revenue code{(codes.Count == 1 ? "" : "s")} {string.Join(", ", codes.Select(rc => $"{rc} ({catalog.RevenueCodeLabel(rc)})"))}";

    /// <summary>"Everett Road ASC LLC, EIN 83-3542382", from the groups the rates were found under.</summary>
    private static string Taxpayers(RevenueProcedureRow row)
    {
        var names = (row.BusinessNames ?? "").Split(" | ", StringSplitOptions.RemoveEmptyEntries);
        var name = names.Length switch
        {
            0 => "another tax ID",
            1 => names[0],
            _ => $"{names[0]} and {names.Length - 1} other name{(names.Length == 2 ? "" : "s")}",
        };
        var eins = (row.Eins ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Ein.Format).ToList();
        return eins.Count == 0 ? name : $"{name}, EIN {string.Join(", ", eins)}";
    }

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
                    [Link($"Search {codeLabel} for all providers", [.. ClearProvider, .. ClearNarrowing])])
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
        var (npi, ein) = (search.NpiText, search.Ein);

        // The NPI has rates for this code, but under a different tax ID than the one entered.
        if (npi is not null && ein is not null && facts.Any(fact => fact.NpiMatch))
        {
            return [Hint("npi_other_ein", "info",
                $"NPI {npi} has no rate for {codeLabel} under {EinLabel(ein)}, but has {codeLabel} rates under " +
                $"{DescribeProviders(facts.Where(fact => fact.NpiMatch))}. Contracts are signed per tax ID, so the NPI and EIN must match.",
                [Link("Search this NPI without the EIN", [("ein", null), ("provider", null), .. ClearNarrowing])])];
        }

        var both = await LookupAsync(search.Npi, ein);
        if (both.GroupCount == 0 && npi is not null && ein is not null)
        {
            var npiOnly = await LookupAsync(search.Npi, null);
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
        && (search.CodeType is null || fact.CodeType == search.CodeType)
        && (search.Group is null || RateSearchAssembler.GroupKeyOf(fact, catalog) == search.Group);

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
            $"This code has rates{(search.ProviderFilterActive ? $" for {FilteredProviders}" : "")}, but none match the filters ({string.Join(", ", applied)}). Settings with rates:",
            settings);
    }

    // ---------- Helpers ----------

    /// <summary>A non-standard NPI is searched anyway (some files publish them); say what's wrong with it.</summary>
    private SearchHint NpiCheck(long npi) =>
        Hint("npi_check_digit", "warning", Domain.Npi.HasStandardPrefix(npi)
            ? $"NPI {search.NpiText} fails the standard NPI check-digit test. Some plan files publish NPIs like this, so the search still ran."
            : $"NPI {search.NpiText} isn't a standard NPI: NPIs are 10 digits starting with 1 or 2. Some plan files publish NPIs like this, so the search still ran.");

    private async Task<ProviderLookupRow> LookupAsync(long? npi, string? ein) =>
        await connection.QuerySingleAsync<ProviderLookupRow>(
            SqlFile.For<SearchHints>("ProviderLookup.sql"), new { snapshotId = snapshot.SnapshotId, codes = search.Candidates, npi, ein });

    /// <summary>Who the provider filters name: one provider for an NPI or EIN, possibly many for a name.</summary>
    private string FilteredProviders => search.IdFilter ? "this provider" : $"providers whose name contains \"{search.NameTyped}\"";

    private string IdLabel() =>
        search.NpiText is { } npi ? $"NPI {npi}" : search.Ein is { } ein ? EinLabel(ein) : "This provider";

    private static string EinLabel(string ein) => ein.StartsWith('u') ? "this tax ID" : $"EIN {Ein.Format(ein)}";

    private static string Lead(string? lead, string message) => lead is null ? message : $"{lead} {message}";

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
        string.Create(CultureInfo.InvariantCulture, $"{count:N0} {noun}{(count == 1 ? "" : "s")}");

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

    /// <summary>Which provider filters a revenue code lookup applies, and which of the searched ones it dropped.</summary>
    private sealed record RevenueScope(long? Npi, string? Ein, string? Name, string? Product)
    {
        public bool OtherProduct { get; init; }
        public bool OtherName { get; init; }
        public bool OtherEin { get; init; }
        public bool Relaxed => OtherProduct || OtherName || OtherEin;
    }

    private sealed record CodeKind(string CodeType, string Code)
    {
        public string Label => $"{CodeType} {Code}";
    }

    /// <summary>A kind of code to offer, and whether the filtered provider has rates under it.</summary>
    private sealed record KindOption(CodeKind Kind, bool ForProvider);

    private sealed record CodeKindRow(string CodeType, string Code, bool Searchable, bool ForProvider);

    private sealed record SimilarCodeRow(string CodeType, string Code, long Rates, string? Name);

    private sealed record RevenueProcedureRow(
        string RevenueCode, string CodeType, string Code, long Rates, long Providers, string? Products, string? Eins, string? BusinessNames);

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
