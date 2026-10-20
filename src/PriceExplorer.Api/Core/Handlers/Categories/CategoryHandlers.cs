using Dapper;
using DuckDB.NET.Data;
using MediatR;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Queries.Categories;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Api.Core.Handlers.Categories;

public sealed class GetCategoriesHandler(DuckDbConnectionFactory connections, CodeReference codeReference)
    : IRequestHandler<GetCategoriesQuery, CategoryTree>
{
    public async Task<CategoryTree> Handle(GetCategoriesQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var snapshot = await SnapshotResolver.ResolveAsync(connection, request.Snapshot);
        var codes = await SnapshotCodes.ListAsync(connection, snapshot.SnapshotId);

        var roots = new Dictionary<string, Node>(StringComparer.Ordinal);
        foreach (var code in codes)
        {
            var level = roots;
            foreach (var step in codeReference.PathOf(code.CodeType, code.Code))
            {
                if (!level.TryGetValue(step.Key, out var node))
                {
                    node = new Node(step);
                    level[step.Key] = node;
                }

                node.Codes++;
                node.Rates += code.Rates;
                level = node.Children;
            }
        }

        return new CategoryTree(new SnapshotRef(snapshot.SnapshotId, snapshot.IndexDate, snapshot.IsLatest), Ordered(roots));
    }

    /// <summary>RBCS categories before code-range ones, then the most rates first.</summary>
    private static List<CategoryNode> Ordered(Dictionary<string, Node> nodes) =>
        nodes.Values
            .OrderBy(node => node.Level.Source == CodeReference.RbcsSource ? 0 : 1)
            .ThenByDescending(node => node.Rates)
            .ThenBy(node => node.Level.Key, StringComparer.Ordinal)
            .Select(node => new CategoryNode(node.Level.Key, node.Level.Label, node.Level.Source, node.Codes, node.Rates, Ordered(node.Children)))
            .ToList();

    private sealed class Node(CategoryLevel level)
    {
        public CategoryLevel Level { get; } = level;
        public int Codes { get; set; }
        public long Rates { get; set; }
        public Dictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);
    }
}

/// <summary>The codes with rates in a snapshot (SnapshotCodeRates.sql), shared by the category tree and the code typeahead's category filter.</summary>
internal static class SnapshotCodes
{
    public static async Task<IReadOnlyList<SnapshotCode>> ListAsync(DuckDBConnection connection, int snapshotId) =>
        (await connection.QueryAsync<SnapshotCode>(SqlFile.For<GetCategoriesHandler>("SnapshotCodeRates.sql"), new { snapshotId })).ToList();
}

internal sealed record SnapshotCode(string CodeType, string Code, long Rates);
