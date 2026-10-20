using System.Globalization;
using Dapper;
using DuckDB.NET.Data;
using MediatR;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Exceptions;
using PriceExplorer.Api.Core.Handlers.Rates;
using PriceExplorer.Api.Core.Models.History;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Queries.History;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Api.Core.Handlers.History;

/// <summary>
/// Compares two snapshots' saved provider values (ChangePairs.sql): per comparison group and provider, changed, new,
/// dropped or unchanged. A product is compared only when both snapshots have it and neither file looks partial.
/// </summary>
internal sealed class GetChangesHandler(DuckDbConnectionFactory connections, ReferenceCatalog catalog)
    : IRequestHandler<GetChangesQuery, ChangesResult>
{
    public const string DefaultSort = "largest";

    public async Task<ChangesResult> Handle(GetChangesQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var to = await SnapshotResolver.ResolveAsync(connection, request.To);
        var from = request.From is { } fromId
            ? await SnapshotResolver.ResolveAsync(connection, fromId)
            : await PreviousAsync(connection, to)
              ?? throw new NotFoundException(
                  $"No snapshot older than {Name(to)} has rate data, so there's nothing to compare it with.", "no_earlier_snapshot");
        if (!IsOlder(from, to))
        {
            throw new BadInputException($"Compare an older snapshot with a newer one: {Name(from)} isn't older than {Name(to)}.", "snapshot_order");
        }

        var (code, codeType) = string.IsNullOrWhiteSpace(request.Code) ? ("", null) : CodeInput.Parse(request.Code, null);
        var codes = code.Length == 0 ? null : CodeInput.Candidates(code, codeType);
        var product = string.IsNullOrWhiteSpace(request.Product) ? null : request.Product.Trim();
        var sort = request.Sort ?? DefaultSort;

        // One transaction: the values, the counts and whether they're current all come from the same moment.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fromState = (await SavedStatistics.StateAsync(connection, from.SnapshotId, transaction)).SingleOrDefault();
        var toState = (await SavedStatistics.StateAsync(connection, to.SnapshotId, transaction)).SingleOrDefault();
        var products = await ProductsAsync(connection, from, to, transaction);
        var compared = products
            .Where(entry => entry.Compared && (product is null || entry.ProductKey == product))
            .Select(entry => entry.ProductKey)
            .ToList();

        var args = new
        {
            from = from.SnapshotId,
            to = to.SnapshotId,
            products = string.Join(',', compared),
            codes = codes is null ? null : string.Join(',', codes),
            codeType,
            kind = request.Kind,
            minChangePct = (double?)request.MinChangePct,
            listedElsewhere = request.ListedElsewhere,
            sort,
            limit = request.PageSize,
            offset = (request.Page - 1) * request.PageSize,
        };
        var pairs = SqlFile.For<GetChangesHandler>("ChangePairs.sql");
        var counts = compared.Count == 0
            ? new ChangeCounts(0, 0, 0, 0, 0, 0, 0, 0)
            : await connection.QuerySingleAsync<ChangeCounts>(pairs + SqlFile.For<GetChangesHandler>("ChangeCounts.sql"), args, transaction);
        var rows = compared.Count == 0
            ? []
            : (await connection.QueryAsync<ChangeRow>(pairs + SqlFile.For<GetChangesHandler>("ChangeRows.sql"), args, transaction)).ToList();
        var names = await CodeNamesAsync(connection, to, rows, transaction);

        // Rate ids point at the rates they describe only while their snapshot's saved statistics are current.
        var fromCurrent = fromState?.Current ?? false;
        var toCurrent = toState?.Current ?? false;
        var total = request.Kind switch
        {
            "changed" => counts.Changed,
            "increased" => counts.Increased,
            "decreased" => counts.Decreased,
            "new" => counts.New,
            "dropped" => counts.Dropped,
            _ => counts.Changed + counts.New + counts.Dropped,
        };

        return new ChangesResult(
            new ChangesEcho(from.SnapshotId, to.SnapshotId, product, code.Length == 0 ? null : code, codeType, request.Kind,
                request.MinChangePct, request.ListedElsewhere, sort, request.Page, request.PageSize),
            Snapshot(from, fromState),
            Snapshot(to, toState),
            fromCurrent && toCurrent,
            products.Select(entry => entry.ToDto()).ToList(),
            counts,
            new RateChangesPage(total, request.Page, request.PageSize, rows.Select(row => new RateChange(
                row.Kind, row.GroupKey, row.ProductKey, row.ProductLabel, row.CodeType, row.Code,
                names.GetValueOrDefault((row.CodeType, row.Code)),
                row.FeeType, RateSearchAssembler.FeeTypeLabelOf(row.FeeType),
                row.SettingKey, catalog.Setting(row.SettingKey)?.Label ?? row.SettingKey,
                row.Modifiers.Length == 0
                    ? []
                    : row.Modifiers.Split(',').Select(modifier => new ModifierRef(modifier, catalog.Modifier(modifier).Label)).ToList(),
                row.NegotiatedType, row.Unit,
                row.ProviderKey, row.Ein, row.EinDisplay, row.EinMasked, row.BusinessName,
                row.FromValue, row.ToValue, row.Change, row.ChangePct is { } pct ? (decimal)pct : null,
                fromCurrent ? row.FromRateId : null,
                toCurrent ? row.ToRateId : null,
                row.FromRates, row.ToRates, row.ListedElsewhere)).ToList()));
    }

    private static async Task<ResolvedSnapshot?> PreviousAsync(DuckDBConnection connection, ResolvedSnapshot to)
    {
        var id = await connection.ExecuteScalarAsync<int?>(
            SqlFile.For<GetChangesHandler>("PreviousSnapshot.sql"), new { snapshotId = to.SnapshotId });
        return id is { } previous ? await SnapshotResolver.ResolveAsync(connection, previous) : null;
    }

    private static bool IsOlder(ResolvedSnapshot from, ResolvedSnapshot to) =>
        from.IndexDate is { } older && to.IndexDate is { } newer ? older < newer : from.SnapshotId < to.SnapshotId;

    /// <summary>Every product either snapshot has, and whether both have it complete enough to compare.</summary>
    private static async Task<List<ProductEntry>> ProductsAsync(
        DuckDBConnection connection, ResolvedSnapshot from, ResolvedSnapshot to, System.Data.Common.DbTransaction transaction)
    {
        var saved = (await connection.QueryAsync<ProductRow>(
            SqlFile.For<GetChangesHandler>("SnapshotProducts.sql"), new { from = from.SnapshotId, to = to.SnapshotId }, transaction)).ToList();
        var completeness = await Completeness.ReadAsync(connection, transaction);

        return saved
            .GroupBy(row => row.ProductKey, StringComparer.Ordinal)
            .Select(rows =>
            {
                var older = rows.FirstOrDefault(row => row.SnapshotId == from.SnapshotId);
                var newer = rows.FirstOrDefault(row => row.SnapshotId == to.SnapshotId);
                var label = (newer ?? older)!.ProductLabel;
                var reason = MissingReason(from, older) ?? MissingReason(to, newer)
                    ?? PartialReason(from, completeness.GetValueOrDefault((from.SnapshotId, rows.Key)))
                    ?? PartialReason(to, completeness.GetValueOrDefault((to.SnapshotId, rows.Key)));
                return new ProductEntry(rows.Key, label, reason);
            })
            .OrderBy(entry => entry.ProductLabel, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Why a product one snapshot lacks is left out: the index doesn't list it, or its file couldn't be loaded.</summary>
    private static string? MissingReason(ResolvedSnapshot snapshot, ProductRow? row) =>
        row is null ? $"Not in the {Name(snapshot)} snapshot."
        : !row.Saved ? $"Left out: the {Name(snapshot)} file couldn't be loaded."
        : null;

    /// <summary>Why a partial file is left out, naming the measure that fell short; null when the file is complete.</summary>
    internal static string? PartialReason(ResolvedSnapshot snapshot, ProductCompleteness? completeness)
    {
        if (completeness is not { Partial: true })
        {
            return null;
        }

        if (completeness.Score is not { } score || score >= SnapshotCompleteness.PartialBelow)
        {
            return $"Left out: the {Name(snapshot)} file was only partly read, so it looks incomplete.";
        }

        var measure = score == completeness.Rates ? "rates" : score == completeness.Eins ? "tax IDs" : "billing codes";
        return string.Create(CultureInfo.InvariantCulture,
            $"Left out: the {Name(snapshot)} file looks incomplete. It has {score * 100:0}% of the {measure} that nearby months have.");
    }

    private static async Task<Dictionary<(string CodeType, string Code), string?>> CodeNamesAsync(
        DuckDBConnection connection, ResolvedSnapshot to, IReadOnlyList<ChangeRow> rows, System.Data.Common.DbTransaction transaction)
    {
        var codes = rows.Select(row => row.Code).Distinct(StringComparer.Ordinal).ToArray();
        if (codes.Length == 0)
        {
            return [];
        }

        var described = await connection.QueryAsync<CodeNameRow>(
            SqlFile.For<SearchRatesHandler>("CodeDescriptions.sql"), new { snapshotId = to.SnapshotId, codes }, transaction);
        return described.ToDictionary(row => (row.CodeType, row.Code), row => row.Name);
    }

    private static HistorySnapshot Snapshot(ResolvedSnapshot snapshot, StatisticsState? state) =>
        new(snapshot.SnapshotId, snapshot.IndexDate, snapshot.IsLatest, UtcTimestamps.From(state?.BuiltAt), state?.Current ?? false);

    private static string Name(ResolvedSnapshot snapshot) =>
        snapshot.IndexDate is { } date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : $"#{snapshot.SnapshotId}";

    private sealed record ProductEntry(string ProductKey, string ProductLabel, string? NotComparedBecause)
    {
        public bool Compared => NotComparedBecause is null;

        public ChangesProduct ToDto() => new(ProductKey, ProductLabel, Compared, NotComparedBecause);
    }

    private sealed record ProductRow(int SnapshotId, string ProductKey, string ProductLabel, bool Saved);

    private sealed record CodeNameRow(string CodeType, string Code, string? Name, string? Description, int FromSnapshot);

    private sealed record ChangeRow(
        string Kind, string GroupKey, string ProductKey, string ProductLabel, string CodeType, string Code, string FeeType,
        string SettingKey, string Modifiers, string NegotiatedType, string Unit, string ProviderKey, string? Ein, string EinDisplay,
        bool EinMasked, string BusinessName, decimal? FromValue, decimal? ToValue, decimal? Change, double? ChangePct,
        long? FromRateId, long? ToRateId, int? FromRates, int? ToRates, bool ListedElsewhere);
}
