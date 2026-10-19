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
        var code = CodeInput.Clean(request.Code);
        var codeType = string.IsNullOrWhiteSpace(request.CodeType) ? null : request.CodeType.Trim().ToUpperInvariant();
        var candidates = CodeInput.Candidates(code, codeType);
        var npi = Npi.Parse(request.Npi);
        ProviderKeys.TryNormalizeEinFilter(request.Ein, out var ein);
        var provider = string.IsNullOrWhiteSpace(request.Provider) ? null : request.Provider.Trim().ToLowerInvariant();
        var sort = request.Sort ?? "rate";
        var providerFilterActive = npi is not null || ein is not null || provider is not null;

        await using var connection = await connections.OpenAsync(cancellationToken);
        var snapshot = await SnapshotResolver.ResolveAsync(connection, request.Snapshot);

        var facts = (await connection.QueryAsync<RateFactRow>(
            SqlFile.For<SearchRatesHandler>("SearchRates.sql"),
            new { snapshotId = snapshot.SnapshotId, codes = candidates, npi, ein, provider })).ToList();

        var assembled = RateSearchAssembler.Assemble(
            facts,
            new AssemblyOptions(
                Blank(request.Product), Blank(request.Setting), Blank(request.FeeType), codeType, Blank(request.Group),
                providerFilterActive, sort, request.Page, request.PageSize),
            catalog);

        var codeInfo = await DescribeCodeAsync(connection, snapshot, facts, candidates);
        var hints = new List<SearchHint>();
        if (npi is { } typedNpi && !Npi.IsValid(typedNpi))
        {
            hints.Add(Hint("npi_check_digit", "warning",
                $"NPI {typedNpi} fails the standard NPI check-digit test. Some plan files publish NPIs like this, so the search still ran."));
        }

        if (facts.Count == 0)
        {
            hints.Add(await CodeNotFoundHintAsync(connection, snapshot, code, candidates));
        }
        else
        {
            hints.AddRange(CodeShapeHints(code, facts));
            if (assembled.Groups.Count == 0)
            {
                hints.AddRange(providerFilterActive && !facts.Any(f => f.FilterMatch)
                    ? await ProviderHintsAsync(connection, snapshot, codeInfo, npi, ein, provider)
                    : [FiltersHideResultsHint(request, assembled.AllGroups)]);
            }
        }

        var echo = new RateSearchEcho(
            code, candidates, codeType, npi?.ToString(CultureInfo.InvariantCulture), ein, Blank(request.Product), Blank(request.Setting),
            Blank(request.FeeType), request.Provider?.Trim(), Blank(request.Group), sort, request.Page, request.PageSize);

        return new RateSearchResult(
            echo,
            new SnapshotRef(snapshot.SnapshotId, snapshot.IndexDate, snapshot.IsLatest),
            codeInfo,
            assembled.Groups,
            assembled.Rows,
            hints,
            assembled.Quality);
    }

    private async Task<CodeInfo?> DescribeCodeAsync(DuckDBConnection connection, ResolvedSnapshot snapshot, List<RateFactRow> facts, IReadOnlyList<string> candidates)
    {
        // The code with the most providers wins when a short number matched several kinds of code.
        var primary = facts
            .GroupBy(fact => (fact.CodeType, fact.Code))
            .OrderByDescending(group => group.Select(fact => fact.ProviderKey).Distinct().Count())
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

    private IEnumerable<SearchHint> CodeShapeHints(string code, List<RateFactRow> facts)
    {
        var kinds = facts.Select(fact => (fact.CodeType, fact.Code)).Distinct().ToList();
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

    private async Task<IEnumerable<SearchHint>> ProviderHintsAsync(
        DuckDBConnection connection, ResolvedSnapshot snapshot, CodeInfo? code, long? npi, string? ein, string? provider)
    {
        var hints = new List<SearchHint>();
        var codeLabel = code is null ? "this code" : $"{code.CodeType} {code.Code}";

        if (npi is not null || ein is not null)
        {
            var both = await LookupAsync(connection, snapshot, npi, ein);
            if (both.GroupCount == 0 && npi is not null && ein is not null)
            {
                var npiOnly = await LookupAsync(connection, snapshot, npi, null);
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
                    $"{who} has no rate for {codeLabel} in these files. It has rates for {both.CodeCount} other code{(both.CodeCount == 1 ? "" : "s")}.",
                    both.TopCodeList.Select(top => Link($"Search {top}", ("code", top), ("codeType", null))).ToList()));
            }
        }

        if (provider is not null)
        {
            hints.Add(Hint("provider_name_no_match", "info",
                $"No provider whose name contains \"{provider}\" has a rate for {codeLabel}.",
                [Link("Clear the name filter", ("provider", null))]));
        }

        return hints;
    }

    private SearchHint FiltersHideResultsHint(SearchRatesQuery request, IReadOnlyList<RateGroup> allGroups)
    {
        var applied = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.Setting)) applied.Add(catalog.Setting(request.Setting)?.Label ?? request.Setting);
        if (!string.IsNullOrWhiteSpace(request.FeeType)) applied.Add(request.FeeType == "facility" ? "facility fee" : "professional fee");
        if (!string.IsNullOrWhiteSpace(request.Product)) applied.Add(request.Product);
        if (!string.IsNullOrWhiteSpace(request.CodeType)) applied.Add(request.CodeType);

        var settings = allGroups
            .Select(group => (group.SettingKey, group.SettingLabel))
            .Distinct()
            .Select(setting => Link(setting.SettingLabel, ("setting", setting.SettingKey), ("feeType", null), ("product", null)))
            .ToList();

        return Hint("filters_hide_results", "info",
            $"This code has rates, but none match the filters ({string.Join(", ", applied)}). Settings with rates:",
            settings);
    }

    private static async Task<ProviderLookupRow> LookupAsync(DuckDBConnection connection, ResolvedSnapshot snapshot, long? npi, string? ein) =>
        await connection.QuerySingleAsync<ProviderLookupRow>(
            SqlFile.For<SearchRatesHandler>("ProviderLookup.sql"), new { snapshotId = snapshot.SnapshotId, npi, ein });

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static SearchHint Hint(string kind, string severity, string message, IReadOnlyList<HintLink>? links = null) =>
        new(kind, severity, message, links ?? []);

    private static HintLink Link(string label, params (string Key, string? Value)[] query) =>
        new(label, query.ToDictionary(pair => pair.Key, pair => pair.Value));

    private sealed record CodeDescriptionRow(string CodeType, string Code, string? Name, string? Description, int FromSnapshot);

    private sealed record SimilarCodeRow(string CodeType, string Code, long Rates, string? Name);

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
