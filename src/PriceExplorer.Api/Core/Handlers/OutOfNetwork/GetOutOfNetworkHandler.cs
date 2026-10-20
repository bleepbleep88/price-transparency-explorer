using Dapper;
using MediatR;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Models.OutOfNetwork;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Queries.OutOfNetwork;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Api.Core.Handlers.OutOfNetwork;

/// <summary>
/// Reads a code's payments from the snapshot's allowed-amount files (OutOfNetworkPayments.sql) and summarizes them per
/// product and fee type (OutOfNetworkSummary). The code is read the way search reads it, so "450", "0450" and "RC 450"
/// all find revenue code 0450.
/// </summary>
public sealed class GetOutOfNetworkHandler(DuckDbConnectionFactory connections, ReferenceCatalog catalog)
    : IRequestHandler<GetOutOfNetworkQuery, OutOfNetworkResult>
{
    public async Task<OutOfNetworkResult> Handle(GetOutOfNetworkQuery request, CancellationToken cancellationToken)
    {
        var (code, codeType) = CodeInput.Parse(request.Code, request.CodeType);
        var candidates = CodeInput.Candidates(code, codeType);

        await using var connection = await connections.OpenAsync(cancellationToken);
        var snapshot = await SnapshotResolver.ResolveAsync(connection, request.Snapshot);
        var files = await connection.QueryAsync<OutOfNetworkFile>(
            SqlFile.For<GetOutOfNetworkHandler>("OutOfNetworkFiles.sql"), new { snapshotId = snapshot.SnapshotId });
        var facts = (await connection.QueryAsync<OutOfNetworkFact>(
                SqlFile.For<GetOutOfNetworkHandler>("OutOfNetworkPayments.sql"), new { snapshotId = snapshot.SnapshotId, codes = candidates }))
            .Where(fact => codeType is null || fact.BillingCodeType == codeType)
            .ToList();

        return new OutOfNetworkResult(
            new OutOfNetworkEcho(code, candidates, codeType),
            new SnapshotRef(snapshot.SnapshotId, snapshot.IndexDate, snapshot.IsLatest),
            [.. files],
            OutOfNetworkSummary.Summarize(facts, catalog));
    }
}
