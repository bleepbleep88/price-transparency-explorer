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
/// exact NPI counts for the page (RowNpis.sql), the code's description, and hints for empty or surprising results.
/// </summary>
public sealed class SearchRatesHandler(DuckDbConnectionFactory connections, ReferenceCatalog catalog)
    : IRequestHandler<SearchRatesQuery, RateSearchResult>
{
    public async Task<RateSearchResult> Handle(SearchRatesQuery request, CancellationToken cancellationToken)
    {
        var search = SearchContext.From(request);
        var sort = request.Sort ?? "rate";

        await using var connection = await connections.OpenAsync(cancellationToken);
        var snapshot = await SnapshotResolver.ResolveAsync(connection, request.Snapshot);

        var facts = (await connection.QueryAsync<RateFactRow>(
            SqlFile.For<SearchRatesHandler>("SearchRates.sql"),
            new { snapshotId = snapshot.SnapshotId, codes = search.Candidates, npi = search.Npi, ein = search.Ein, provider = search.Name })).ToList();

        var assembled = RateSearchAssembler.Assemble(
            facts,
            new AssemblyOptions(
                search.Product, search.Setting, search.FeeType, search.CodeType, search.Group,
                search.ProviderFilterActive, sort, request.Page, request.PageSize),
            catalog);
        var rows = await CountRowNpisAsync(connection, assembled.Rows);

        var codeInfo = await DescribeCodeAsync(connection, snapshot, facts);
        var hints = await new SearchHints(connection, snapshot, catalog, search).ForAsync(facts, assembled, codeInfo);

        var echo = new RateSearchEcho(
            search.Code, search.Candidates, search.CodeType, search.Npi?.ToString(CultureInfo.InvariantCulture), search.Ein,
            search.Product, search.Setting, search.FeeType, search.NameTyped, search.Group, sort, request.Page, request.PageSize);

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

    private async Task<CodeInfo?> DescribeCodeAsync(DuckDBConnection connection, ResolvedSnapshot snapshot, List<RateFactRow> facts)
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

    private sealed record CodeDescriptionRow(string CodeType, string Code, string? Name, string? Description, int FromSnapshot);

    private sealed record RowNpiCount(long RowIndex, long Npis);
}
