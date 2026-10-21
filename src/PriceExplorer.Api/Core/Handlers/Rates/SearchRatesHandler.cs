using System.Data.Common;
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

/// <summary>
/// Runs a rate search: one query for the facts (SearchRates.sql), pure grouping and statistics (RateSearchAssembler),
/// exact NPI counts and who is behind each row on the page (ProviderProfiles.sql), the code's description, and hints for
/// empty or surprising results.
/// </summary>
public sealed class SearchRatesHandler(
    DuckDbConnectionFactory connections, ReferenceCatalog catalog, ProviderDirectoryInfo directory, CodeReference codes,
    ILogger<SearchRatesHandler> logger)
    : IRequestHandler<SearchRatesQuery, RateSearchResult>
{
    public async Task<RateSearchResult> Handle(SearchRatesQuery request, CancellationToken cancellationToken)
    {
        var search = SearchContext.From(request);
        var sort = request.Sort ?? "rate";

        await using var connection = await connections.OpenAsync(cancellationToken);
        var snapshot = await SnapshotResolver.ResolveAsync(connection, request.Snapshot);
        if (search.County is not null && directory.IsLoaded)
        {
            search = search with
            {
                CountyPlace = await connection.QuerySingleOrDefaultAsync<string?>(
                    SqlFile.For<SearchRatesHandler>("CountyPlace.sql"), new { county = search.County }),
            };
        }

        var (facts, assembled) = await RunAsync(connection, snapshot, search, sort, request.Page, request.PageSize, catalog);
        var rows = await ProfileRowsAsync(connection, assembled.Rows, assembled.Groups, search);

        var codeInfo = await DescribeCodeAsync(connection, snapshot, facts, assembled.Groups, search.CodeType);
        var hints = await new SearchHints(connection, snapshot, catalog, codes, search).ForAsync(facts, assembled, codeInfo);
        hints = await new HintLinkChecker(connections, snapshot, catalog, logger)
            .KeepLinksThatFindRatesAsync(request, search, facts, hints, cancellationToken);

        var echo = new RateSearchEcho(
            search.Code, search.Candidates, search.CodeType, search.NpiText, search.Ein,
            search.Product, search.Setting, search.FeeType, search.NameTyped, search.SpecialtyTyped, search.CountyTyped, search.CountyPlace, search.Group, sort,
            request.Page, request.PageSize);

        return new RateSearchResult(
            echo,
            new SnapshotRef(snapshot.SnapshotId, snapshot.IndexDate, snapshot.IsLatest),
            codeInfo,
            assembled.Groups,
            rows,
            hints,
            assembled.Quality);
    }

    /// <summary>The facts for a search (SearchRates.sql) and their groups and rows; hint links are checked the same way.</summary>
    internal static async Task<(List<RateFactRow> Facts, AssembledSearch Assembled)> RunAsync(
        DuckDBConnection connection, ResolvedSnapshot snapshot, SearchContext search, string sort, int page, int pageSize,
        ReferenceCatalog catalog)
    {
        var facts = await QueryFactsAsync(
            connection, snapshot.SnapshotId, search.Candidates, search.Npi, search.Ein, search.Name, search.Specialty, search.County);

        // DRG 0470 never means revenue code 0470: a foreign code type only matches rates of that type (none in these files).
        if (search.ForeignCodeType)
        {
            facts = facts.Where(fact => fact.CodeType == search.CodeType).ToList();
        }

        return (facts, Assemble(facts, search, sort, page, pageSize, catalog));
    }

    /// <summary>Groups and rows for a search's facts under its narrowing filters.</summary>
    internal static AssembledSearch Assemble(
        IReadOnlyList<RateFactRow> facts, SearchContext search, string sort, int page, int pageSize, ReferenceCatalog catalog) =>
        RateSearchAssembler.Assemble(
            facts,
            new AssemblyOptions(
                search.Product, search.Setting, search.FeeType, search.CodeType, search.Group,
                search.ProviderFilterActive, sort, page, pageSize),
            catalog);

    /// <summary>
    /// One row per rate × place of service × provider for the codes (stored forms), with each provider filter's match
    /// flags; null filters match everyone. The saved statistics are built from this same query, with no filters.
    /// </summary>
    internal static async Task<List<RateFactRow>> QueryFactsAsync(
        DuckDBConnection connection, int snapshotId, IReadOnlyList<string> codes, long? npi = null, string? ein = null,
        string? provider = null, string? specialty = null, string? county = null, DbTransaction? transaction = null) =>
        (await connection.QueryAsync<RateFactRow>(
            SqlFile.For<SearchRatesHandler>("SearchRates.sql"),
            new { snapshotId, codes, npi, ein, provider, specialty, county },
            transaction)).ToList();

    /// <summary>
    /// For the rows on this page: the exact count of distinct NPIs behind each row (the assembler only sees per-rate
    /// counts) and, with the provider directory loaded, who they are and any "check this" flags. Both cover every rate
    /// behind a row, so the profile, the NPI count and the NPI list agree.
    /// </summary>
    private async Task<RateRowsPage> ProfileRowsAsync(
        DuckDBConnection connection, RateRowsPage page, IReadOnlyList<RateGroup> groups, SearchContext search)
    {
        if (page.Items.Count == 0)
        {
            return page;
        }

        var parameters = new DynamicParameters(new { specialty = search.Specialty, county = search.County });
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

        var sql = SqlFile.For<SearchRatesHandler>("ProviderProfiles.sql").Replace("{rows}", string.Join(", ", values), StringComparison.Ordinal);
        var lines = (await connection.QueryAsync<ProfileLine>(sql, parameters)).ToLookup(line => (int)line.RowIndex);
        var groupsByKey = groups.ToDictionary(group => group.Key, StringComparer.Ordinal);

        return page with
        {
            Items = page.Items.Select((row, index) =>
            {
                var rowLines = lines[index].ToList();
                if (rowLines.FirstOrDefault(line => line.Kind == "totals") is not { } totals)
                {
                    return row;
                }

                var counted = row with { NpiCount = (int)totals.Npis };
                if (!directory.IsLoaded || totals.Npis == 0)
                {
                    return counted;
                }

                var places = rowLines.Where(line => line.Kind == "place")
                    .OrderByDescending(line => line.Matches).ThenByDescending(line => line.Npis).ThenBy(line => line.Label, StringComparer.Ordinal)
                    .ToList();
                var profile = new ProviderProfile(
                    (int)totals.Npis,
                    (int)totals.Found,
                    (int)totals.Individuals,
                    (int)totals.Deactivated,
                    rowLines.Where(line => line.Kind == "specialty")
                        .OrderByDescending(line => line.Matches).ThenByDescending(line => line.Npis).ThenBy(line => line.Label, StringComparer.Ordinal)
                        .Select(line => new SpecialtyCount(line.Label!, line.GroupName, (int)line.Npis))
                        .ToList(),
                    places.Take(3).Select(line => line.Label!).ToList(),
                    places.Count)
                {
                    ImagingReaders = (int)totals.ImagingReaders,
                    CardiacReaders = (int)totals.CardiacReaders,
                };

                return counted with
                {
                    Profile = profile,
                    Flags = groupsByKey.TryGetValue(row.GroupKey, out var group) ? ProviderFlags.For(group, profile) : [],
                };
            }).ToList(),
        };
    }

    private async Task<CodeInfo?> DescribeCodeAsync(
        DuckDBConnection connection, ResolvedSnapshot snapshot, List<RateFactRow> facts, IReadOnlyList<RateGroup> shown, string? codeType)
    {
        // When a short number matched several kinds of code, describe one that's shown under the current filters. When
        // nothing is shown, prefer the kind asked for, then one the filtered provider has, then the one with most providers.
        var shownKinds = shown.Select(group => (group.CodeType, group.Code)).ToHashSet();
        var primary = facts
            .GroupBy(fact => (fact.CodeType, fact.Code))
            .OrderByDescending(group => shownKinds.Contains(group.Key))
            .ThenByDescending(group => group.Key.CodeType == codeType)
            .ThenByDescending(group => group.Any(fact => fact.FilterMatch))
            .ThenByDescending(group => group.Any(fact => fact.IdMatch))
            .ThenByDescending(group => group.Any(fact => fact.NpiMatch))
            .ThenByDescending(group => group.Select(fact => fact.ProviderKey).Distinct().Count())
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

        // CMS's HCPCS Level II name: the fallback when the plan names nothing, and the standard wording of a drug's unit.
        var cmsName = primary.CodeType == BillingCodes.Hcpcs
            ? await connection.QueryFirstOrDefaultAsync<string?>(SqlFile.For<SearchRatesHandler>("HcpcsName.sql"), new { code = primary.Code })
            : null;
        // A CPT or CDT code is named from CMS's RBCS, never the AMA's or ADA's text (PublishedText).
        var rbcsName = PublishedText.IsLicensed(primary.Code) ? codes.RbcsName(primary.CodeType, primary.Code) : null;
        var (name, description, source) = PublishedText.IsLicensed(primary.Code) ? (rbcsName, null, rbcsName is null ? "none" : "rbcs")
            : described is not null ? (described.Name, described.Description, "payer_file")
            : cmsName is not null ? (CodeNames.Short(cmsName), CodeNames.Short(cmsName) == cmsName ? null : cmsName, "cms_hcpcs")
            : (null, null, "none");
        var category = ServiceCategories.Classify(primary.CodeType, primary.Code);
        var doseUnit = category.Key == ServiceCategories.DrugsKey
            ? DoseUnits.Parse(cmsName) ?? DoseUnits.Parse(described?.Name) ?? DoseUnits.Parse(described?.Description)
            : null;

        return new CodeInfo(
            primary.CodeType,
            primary.Code,
            name,
            description,
            source,
            category,
            codes.Classify(primary.CodeType, primary.Code),
            doseUnit,
            ServiceCategories.LevelFamily(primary.CodeType, primary.Code),
            foundVia);
    }

    private sealed record CodeDescriptionRow(string CodeType, string Code, string? Name, string? Description, int FromSnapshot);

    /// <summary>One line of ProviderProfiles.sql: a row's totals, or one of its specialties or places.</summary>
    private sealed record ProfileLine(
        long RowIndex, string Kind, string? Label, string? GroupName, long Npis, long Found, long Individuals, long Deactivated,
        long ImagingReaders, long CardiacReaders, bool Matches);
}
