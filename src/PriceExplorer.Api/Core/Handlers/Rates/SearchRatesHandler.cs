using System.Globalization;
using Dapper;
using DuckDB.NET.Data;
using MediatR;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Queries.Rates;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Api.Core.Handlers.Rates;

public sealed class SearchRatesHandler(DuckDbConnectionFactory connections, ReferenceCatalog catalog)
    : IRequestHandler<SearchRatesQuery, RateSearchResult>
{
    public async Task<RateSearchResult> Handle(SearchRatesQuery request, CancellationToken cancellationToken)
    {
        var (code, codeType) = CodeInput.Parse(request.Code, request.CodeType);
        var candidates = CodeInput.Candidates(code, codeType);
        var npi = Npi.Parse(request.Npi);
        ProviderKeys.TryNormalizeEinFilter(request.Ein, out var ein);
        var nameTyped = Blank(request.Provider);
        var name = nameTyped?.ToLowerInvariant();
        var sort = request.Sort ?? "rate";
        var providerFilterActive = npi is not null || ein is not null || name is not null;

        await using var connection = await connections.OpenAsync(cancellationToken);
        var snapshot = await SnapshotResolver.ResolveAsync(connection, request.Snapshot);

        var facts = (await connection.QueryAsync<RateFactRow>(
            SqlFile.For<SearchRatesHandler>("SearchRates.sql"),
            new { snapshotId = snapshot.SnapshotId, codes = candidates, npi, ein, provider = name })).ToList();

        var assembled = RateSearchAssembler.Assemble(
            facts,
            new AssemblyOptions(
                Blank(request.Product), Blank(request.Setting), Blank(request.FeeType), codeType, Blank(request.Group),
                providerFilterActive, sort, request.Page, request.PageSize),
            catalog);
        var rows = await CountRowNpisAsync(connection, assembled.Rows);

        var codeInfo = await DescribeCodeAsync(connection, snapshot, facts, candidates);
        var hints = new List<SearchHint>();
        if (npi is { } typedNpi && !Npi.IsValid(typedNpi))
        {
            hints.Add(Hint("npi_check_digit", "warning",
                $"NPI {typedNpi} fails the standard NPI check-digit test. Some plan files publish NPIs like this, so the search still ran."));
        }

        if (facts.Count == 0)
        {
            hints.AddRange(await NotFoundHintsAsync(connection, snapshot, code, codeType, candidates));
        }
        else
        {
            hints.AddRange(CodeShapeHints(code, facts));
            var revenueCodes = facts.Where(fact => fact.CodeType == BillingCodes.RevenueCode).Select(fact => fact.Code).Distinct().ToList();
            if (revenueCodes.Count > 0 && await RevenueCodeProceduresHintAsync(connection, snapshot, revenueCodes, directRatesFound: true) is { } procedures)
            {
                hints.Add(procedures);
            }

            if (assembled.Groups.Count == 0)
            {
                if (codeType is not null && facts.All(fact => fact.CodeType != codeType))
                {
                    hints.Add(CodeTypeMismatchHint(code, codeType, facts.Select(fact => new CodeKindRow(fact.CodeType, fact.Code)).Distinct().ToList()));
                }
                else if (providerFilterActive && !facts.Any(fact => fact.FilterMatch))
                {
                    hints.AddRange(await ProviderHintsAsync(connection, snapshot, codeInfo, candidates, facts, npi, ein, nameTyped));
                }
                else
                {
                    hints.Add(FiltersHideResultsHint(request, codeType, assembled.AllGroups, providerFilterActive));
                }
            }
        }

        var echo = new RateSearchEcho(
            code, candidates, codeType, npi?.ToString(CultureInfo.InvariantCulture), ein, Blank(request.Product), Blank(request.Setting),
            Blank(request.FeeType), nameTyped, Blank(request.Group), sort, request.Page, request.PageSize);

        return new RateSearchResult(
            echo,
            new SnapshotRef(snapshot.SnapshotId, snapshot.IndexDate, snapshot.IsLatest),
            codeInfo,
            assembled.Groups,
            rows,
            hints,
            assembled.Quality);
    }

    /// <summary>Exact distinct-NPI counts for the rows on this page (the assembler only sees per-rate counts).</summary>
    private static async Task<RateRowsPage> CountRowNpisAsync(DuckDBConnection connection, RateRowsPage page)
    {
        if (page.Items.Count == 0)
        {
            return page;
        }

        var parameters = new DynamicParameters();
        var values = new List<string>();
        for (var i = 0; i < page.Items.Count; i++)
        {
            parameters.Add($"k{i}", page.Items[i].ProviderKey);
            foreach (var rateId in page.Items[i].RateIds)
            {
                // Row index and rate id are numbers we generated; only the provider key is passed as a parameter.
                values.Add(string.Create(CultureInfo.InvariantCulture, $"({i}, $k{i}, {rateId})"));
            }
        }

        var sql = SqlFile.For<SearchRatesHandler>("RowNpis.sql").Replace("{rows}", string.Join(", ", values), StringComparison.Ordinal);
        var counts = (await connection.QueryAsync<RowNpiCount>(sql, parameters)).ToDictionary(count => (int)count.RowIndex, count => (int)count.Npis);
        return page with
        {
            Items = page.Items.Select((row, index) => counts.TryGetValue(index, out var npis) ? row with { NpiCount = npis } : row).ToList(),
        };
    }

    private async Task<CodeInfo?> DescribeCodeAsync(DuckDBConnection connection, ResolvedSnapshot snapshot, List<RateFactRow> facts, IReadOnlyList<string> candidates)
    {
        // The code with the most providers wins when a short number matched several kinds of code.
        var primary = facts
            .GroupBy(fact => (fact.CodeType, fact.Code))
            .OrderByDescending(group => group.Select(fact => fact.ProviderKey).Distinct().Count())
            .ThenBy(group => group.Key.CodeType, StringComparer.Ordinal)
            .Select(group => group.Key)
            .FirstOrDefault();
        if (primary == default)
        {
            return null;
        }

        var described = await connection.QueryFirstOrDefaultAsync<CodeDescriptionRow>(
            SqlFile.For<SearchRatesHandler>("CodeDescriptions.sql"),
            new { snapshotId = snapshot.SnapshotId, codes = new[] { primary.Code } });

        var foundVia = facts
            .Where(fact => fact.CodeType == primary.CodeType && fact.Code == primary.Code)
            .Select(fact => fact.CodeSource == "additional_information"
                ? $"Facility rates under revenue code {fact.BillingCode} ({catalog.RevenueCodeLabel(fact.BillingCode)})"
                : $"Billing code ({fact.BillingCodeType})")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        return new CodeInfo(
            primary.CodeType,
            primary.Code,
            described?.Name,
            described?.Description,
            described is null ? "none" : "payer_file",
            ServiceCategories.Classify(primary.CodeType, primary.Code),
            ServiceCategories.LevelFamily(primary.CodeType, primary.Code),
            foundVia);
    }

    /// <summary>Nothing matched: a revenue code filed under procedure codes, a wrong code type, or a code not in the files.</summary>
    private async Task<IReadOnlyList<SearchHint>> NotFoundHintsAsync(
        DuckDBConnection connection, ResolvedSnapshot snapshot, string code, string? codeType, IReadOnlyList<string> candidates)
    {
        if (await RevenueCodeProceduresHintAsync(connection, snapshot, candidates, directRatesFound: false) is { } procedures)
        {
            return [procedures];
        }

        if (codeType is not null)
        {
            var kinds = (await connection.QueryAsync<CodeKindRow>(
                SqlFile.For<SearchRatesHandler>("CodeTypesFor.sql"),
                new { snapshotId = snapshot.SnapshotId, codes = CodeInput.Candidates(code, null) })).ToList();
            if (kinds.Count > 0)
            {
                return [CodeTypeMismatchHint(code, codeType, kinds)];
            }
        }

        return [await CodeNotFoundHintAsync(connection, snapshot, code, candidates)];
    }

    private async Task<SearchHint> CodeNotFoundHintAsync(DuckDBConnection connection, ResolvedSnapshot snapshot, string code, IReadOnlyList<string> candidates)
    {
        var longest = candidates.OrderByDescending(candidate => candidate.Length).First();
        var prefix = longest[..Math.Min(3, longest.Length)];
        var similar = (await connection.QueryAsync<SimilarCodeRow>(
            SqlFile.For<SearchRatesHandler>("SimilarCodes.sql"), new { snapshotId = snapshot.SnapshotId, prefix })).ToList();

        var message = $"No rates for {code} in the {RateSearchFormat.SnapshotDate(snapshot.IndexDate)} snapshot. " +
            "These files list outpatient, fee-for-service rates only, so inpatient stays (billed by DRG) and many specialty codes won't appear.";
        if (similar.Count > 0)
        {
            message += " Codes that start the same way:";
        }

        return Hint("code_not_found", "info", message, similar
            .Select(row => Link($"{row.CodeType} {row.Code}{(row.Name is null ? "" : $" · {row.Name}")}", ("code", row.Code), ("codeType", row.CodeType)))
            .ToList());
    }

    private static SearchHint CodeTypeMismatchHint(string code, string codeType, IReadOnlyList<CodeKindRow> kinds) =>
        Hint("code_type_mismatch", "info",
            $"No {codeType} rates for {code}. It's in these files as {string.Join(" and ", kinds.Select(kind => $"{kind.CodeType} {kind.Code}"))}.",
            kinds.Select(kind => Link($"Search {kind.CodeType} {kind.Code}", ("code", kind.Code), ("codeType", kind.CodeType))).ToList());

    /// <summary>
    /// Facility rates under a revenue code are usually filed under the procedure code in their notes field, so searching the
    /// revenue code itself misses them. Points to the procedure codes instead.
    /// </summary>
    private async Task<SearchHint?> RevenueCodeProceduresHintAsync(
        DuckDBConnection connection, ResolvedSnapshot snapshot, IReadOnlyList<string> revenueCodes, bool directRatesFound)
    {
        var procedures = (await connection.QueryAsync<RevenueProcedureRow>(
            SqlFile.For<SearchRatesHandler>("RevenueCodeProcedures.sql"), new { snapshotId = snapshot.SnapshotId, codes = revenueCodes })).ToList();
        if (procedures.Count == 0)
        {
            return null;
        }

        var revenueLabel = string.Join(", ", procedures.Select(row => row.RevenueCode).Distinct().Order()
            .Select(rc => $"revenue code {rc} ({catalog.RevenueCodeLabel(rc)})"));
        var total = procedures.Sum(row => row.Rates);
        var codes = procedures.Select(row => (row.CodeType, row.Code)).Distinct().Count();
        var message = directRatesFound
            ? $"Another {total:N0} facility rates under {revenueLabel} are filed under the procedure code in their notes field " +
              $"({codes:N0} codes), so they appear when you search that procedure code. Most common:"
            : $"All {total:N0} facility rates under {revenueLabel} are filed under the procedure code in their notes field " +
              $"({codes:N0} codes). Search a procedure code to compare like for like. Most common:";

        return Hint("revenue_code_procedures", "info", message, procedures
            .GroupBy(row => (row.CodeType, row.Code))
            .Select(group => (group.Key.CodeType, group.Key.Code, Rates: group.Sum(row => row.Rates)))
            .OrderByDescending(row => row.Rates)
            .ThenBy(row => row.Code, StringComparer.Ordinal)
            .Take(5)
            .Select(row => Link($"{row.CodeType} {row.Code} · {row.Rates:N0} rates", ("code", row.Code), ("codeType", row.CodeType)))
            .ToList());
    }

    private IEnumerable<SearchHint> CodeShapeHints(string code, List<RateFactRow> facts)
    {
        var kinds = facts.Select(fact => (fact.CodeType, fact.Code)).Distinct().OrderBy(kind => kind.CodeType, StringComparer.Ordinal).ToList();
        if (kinds.Count > 1)
        {
            yield return Hint("ambiguous_code", "info",
                $"\"{code}\" matches more than one kind of code. Each shows as its own group; pick one to narrow the results.",
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
    /// The provider filters matched no rate for this code. Says which filter is the reason: the NPI/EIN has no rate for
    /// the code, no provider name matches, or each matches on its own but never in the same provider group.
    /// </summary>
    private async Task<IEnumerable<SearchHint>> ProviderHintsAsync(
        DuckDBConnection connection, ResolvedSnapshot snapshot, CodeInfo? code, IReadOnlyList<string> candidates,
        List<RateFactRow> facts, long? npi, string? ein, string? nameTyped)
    {
        var hints = new List<SearchHint>();
        var codeLabel = code is null ? "this code" : $"{code.CodeType} {code.Code}";
        var idFilter = npi is not null || ein is not null;
        var idProblem = idFilter && !facts.Any(fact => fact.IdMatch);
        var nameProblem = nameTyped is not null && !facts.Any(fact => fact.NameMatch);

        if (idProblem)
        {
            var both = await LookupAsync(connection, snapshot, candidates, npi, ein);
            if (both.GroupCount == 0 && npi is not null && ein is not null)
            {
                var npiOnly = await LookupAsync(connection, snapshot, candidates, npi, null);
                hints.Add(npiOnly.GroupCount == 0
                    ? Hint("npi_not_listed", "info", $"NPI {npi} isn't listed in this snapshot's files.")
                    : Hint("npi_other_ein", "info",
                        $"NPI {npi} is listed under a different tax ID: {npiOnly.Describe()}. Contracts are signed per tax ID, so the NPI and EIN must match.",
                        [Link("Search this NPI without the EIN", ("ein", null))]));
            }
            else if (both.GroupCount == 0)
            {
                hints.Add(npi is not null
                    ? Hint("npi_not_listed", "info", $"NPI {npi} isn't listed in this snapshot's files.")
                    : Hint("ein_not_listed", "info", $"{(ein!.StartsWith('u') ? "This provider" : $"EIN {Ein.Format(ein)}")} isn't listed in this snapshot's files."));
            }
            else
            {
                var who = npi is not null ? $"NPI {npi} ({both.Describe()})" : both.Describe();
                hints.Add(Hint("provider_no_rate", "info",
                    both.CodeCount == 0
                        ? $"{who} has no rate for {codeLabel} in these files."
                        : $"{who} has no rate for {codeLabel} in these files. It has rates for {both.CodeCount:N0} other code{(both.CodeCount == 1 ? "" : "s")}.",
                    both.TopCodeList.Select(top => Link($"Search {top}", ("code", top), ("codeType", null))).ToList()));
            }
        }

        if (nameProblem)
        {
            hints.Add(Hint("provider_name_no_match", "info",
                $"No provider whose name contains \"{nameTyped}\" has a rate for {codeLabel}.",
                [Link("Clear the name filter", ("provider", null))]));
        }

        // Both filters match on their own, but never in the same provider group.
        if (idFilter && nameTyped is not null && !idProblem && !nameProblem)
        {
            var who = npi is not null ? $"NPI {npi}" : ein!.StartsWith('u') ? "This provider" : $"EIN {Ein.Format(ein)}";
            hints.Add(Hint("provider_name_no_match", "info",
                $"{who} has rates for {codeLabel}, but not under a name containing \"{nameTyped}\".",
                [Link("Clear the name filter", ("provider", null))]));
        }

        return hints;
    }

    /// <summary>The code has rates, but the setting / fee type / product / code type filters hide them all.</summary>
    private SearchHint FiltersHideResultsHint(SearchRatesQuery request, string? codeType, IReadOnlyList<RateGroup> allGroups, bool providerFilterActive)
    {
        var applied = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.Setting)) applied.Add(catalog.Setting(request.Setting)?.Label ?? request.Setting);
        if (!string.IsNullOrWhiteSpace(request.FeeType)) applied.Add(request.FeeType == "facility" ? "facility fee" : "professional fee");
        if (!string.IsNullOrWhiteSpace(request.Product)) applied.Add(request.Product);
        if (codeType is not null) applied.Add(codeType);

        // Only offer settings that really have rates for this search: with a provider filter, that provider's groups
        // (a group lists the filtered provider among its highlights). Each link clears the other narrowing filters.
        var settings = allGroups
            .Where(group => !providerFilterActive || group.Highlights.Count > 0)
            .Select(group => (group.SettingKey, group.SettingLabel))
            .Distinct()
            .Select(setting => Link(setting.SettingLabel, ("setting", setting.SettingKey), ("feeType", null), ("product", null), ("codeType", null)))
            .ToList();

        return Hint("filters_hide_results", "info",
            $"This code has rates{(providerFilterActive ? " for this provider" : "")}, but none match the filters ({string.Join(", ", applied)}). Settings with rates:",
            settings);
    }

    private static async Task<ProviderLookupRow> LookupAsync(
        DuckDBConnection connection, ResolvedSnapshot snapshot, IReadOnlyList<string> searchedCodes, long? npi, string? ein) =>
        await connection.QuerySingleAsync<ProviderLookupRow>(
            SqlFile.For<SearchRatesHandler>("ProviderLookup.sql"), new { snapshotId = snapshot.SnapshotId, codes = searchedCodes, npi, ein });

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static SearchHint Hint(string kind, string severity, string message, IReadOnlyList<HintLink>? links = null) =>
        new(kind, severity, message, links ?? []);

    private static HintLink Link(string label, params (string Key, string? Value)[] query) =>
        new(label, query.ToDictionary(pair => pair.Key, pair => pair.Value));

    private sealed record CodeDescriptionRow(string CodeType, string Code, string? Name, string? Description, int FromSnapshot);

    private sealed record SimilarCodeRow(string CodeType, string Code, long Rates, string? Name);

    private sealed record CodeKindRow(string CodeType, string Code);

    private sealed record RevenueProcedureRow(string RevenueCode, string CodeType, string Code, long Rates);

    private sealed record RowNpiCount(long RowIndex, long Npis);

    private sealed record ProviderLookupRow(long GroupCount, string? Eins, bool? AnyUnverified, string? BusinessNames, long CodeCount, string? TopCodes)
    {
        public IReadOnlyList<string> TopCodeList => (TopCodes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);

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
