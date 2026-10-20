using System.Data.Common;
using System.Globalization;
using Dapper;
using DuckDB.NET.Data;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Handlers.Rates;
using PriceExplorer.Api.Core.Models.Insights;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Api.Core.Handlers.Insights;

/// <summary>What the insight handlers share: freshness, code names, group labels and links to the search behind each number.</summary>
internal static class InsightSupport
{
    /// <summary>
    /// Which saved statistics an insight reads, and whether they're current by the statistics' own rule
    /// (<see cref="SavedStatistics"/>: built from the rate files saved now, by this build, with no rate expired since).
    /// Read in the handler's transaction, so the flag and the rows describe the same moment.
    /// </summary>
    public static async Task<StatisticsBasis> BasisAsync(DuckDBConnection connection, ResolvedSnapshot snapshot, DbTransaction transaction)
    {
        var state = (await SavedStatistics.StateAsync(connection, snapshot.SnapshotId, transaction)).SingleOrDefault();
        return new StatisticsBasis(
            new SnapshotRef(snapshot.SnapshotId, snapshot.IndexDate, snapshot.IsLatest),
            UtcTimestamps.From(state?.BuiltAt),
            state?.Current ?? false);
    }

    /// <summary>
    /// Each code's name as the plan's files publish it (the search's own lookup, CodeDescriptions.sql): the short name,
    /// else the description; missing when the files have neither.
    /// </summary>
    public static async Task<IReadOnlyDictionary<(string CodeType, string Code), string>> CodeNamesAsync(
        DuckDBConnection connection, int snapshotId, IEnumerable<string> codes, DbTransaction transaction)
    {
        var distinct = codes.Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length == 0)
        {
            return new Dictionary<(string, string), string>();
        }

        var rows = await connection.QueryAsync<CodeNameRow>(
            SqlFile.For<SearchRatesHandler>("CodeDescriptions.sql"), new { snapshotId, codes = distinct }, transaction);
        return rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Name ?? row.Description))
            .ToDictionary(row => (row.CodeType, row.Code), row => (row.Name ?? row.Description)!.Trim());
    }

    /// <summary>
    /// What to call a code: its name in the plan's files, else for a revenue code (which the files never name) the short
    /// label search shows ("Operating room"), else nothing.
    /// </summary>
    public static string? NameOf(
        IReadOnlyDictionary<(string CodeType, string Code), string> names, ReferenceCatalog catalog, string codeType, string code) =>
        names.TryGetValue((codeType, code), out var name) ? name
        : codeType == BillingCodes.RevenueCode ? catalog.RevenueCodeLabel(code)
        : null;

    public static IReadOnlyList<ModifierRef> Modifiers(string modifiers, ReferenceCatalog catalog) =>
        modifiers.Length == 0 ? [] : modifiers.Split(',').Select(code => new ModifierRef(code, catalog.Modifier(code).Label)).ToList();

    public static string SettingLabel(string settingKey, ReferenceCatalog catalog) => catalog.Setting(settingKey)?.Label ?? settingKey;

    public static int SettingOrder(string settingKey, ReferenceCatalog catalog) => catalog.Setting(settingKey)?.SortOrder ?? int.MaxValue;

    /// <summary>
    /// Query parameters for the search that opens a comparison group: its code and code type, the group (which narrows
    /// the rows to it), and with a provider key, only that provider's rows. The snapshot is named unless it's the default.
    /// </summary>
    public static IReadOnlyDictionary<string, string> SearchLink(
        ResolvedSnapshot snapshot, string codeType, string code, string groupKey, string? providerKey = null)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["code"] = code,
            ["codeType"] = codeType,
            ["group"] = groupKey,
        };
        if (providerKey is not null)
        {
            query["ein"] = providerKey;
        }

        if (!snapshot.IsLatest)
        {
            query["snapshot"] = snapshot.SnapshotId.ToString(CultureInfo.InvariantCulture);
        }

        return query;
    }

    /// <summary>Optional text filters arrive as typed; blank means no filter.</summary>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Codes for a SQL filter: stored forms joined with ',' (see the .sql files), or null for every code.</summary>
    public static string? CodeList(IReadOnlyList<string> codes) => codes.Count == 0 ? null : string.Join(',', codes);

    private sealed record CodeNameRow(string CodeType, string Code, string? Name, string? Description, int FromSnapshot);
}
