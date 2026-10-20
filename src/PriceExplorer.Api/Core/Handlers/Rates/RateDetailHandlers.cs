using Dapper;
using MediatR;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Domain;
using PriceExplorer.Api.Core.Exceptions;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Queries.Rates;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Api.Core.Handlers.Rates;

public sealed class GetRateSourceHandler(DuckDbConnectionFactory connections, ReferenceCatalog catalog)
    : IRequestHandler<GetRateSourceQuery, RateSource>
{
    public async Task<RateSource> Handle(GetRateSourceQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<SourceRow>(
            SqlFile.For<GetRateSourceHandler>("RateSource.sql"), new { rateId = request.RateId })
            ?? throw new NotFoundException($"Rate {request.RateId} doesn't exist. It may come from a snapshot that was reloaded.", "rate_not_found");

        var groups = await connection.QueryAsync<GroupRow>(
            SqlFile.For<GetRateSourceHandler>("RateSourceGroups.sql"), new { rateId = request.RateId, provider = ProviderKeys.NormalizeKey(request.Provider) });
        var groupCount = await connection.ExecuteScalarAsync<long>(
            """
            SELECT count(*)
            FROM rate_provider_refs rr
            JOIN provider_groups pg ON pg.file_id = rr.file_id AND pg.provider_ref_id = rr.provider_ref_id
            WHERE rr.rate_id = $rateId
            """,
            new { rateId = request.RateId });

        return new RateSource(
            row.RateId,
            new RateSourceFile(row.FileId, row.ProductKey, row.ProductLabel, row.FileUrl, row.Sha256, row.FileLastUpdatedOn,
                UtcTimestamps.From(row.HttpLastModified), row.ContentLength, row.ReportingEntityName, row.SchemaVersion),
            new RateSourceSnapshot(row.SnapshotId, row.IndexUrl, row.IndexDate, row.IndexLastUpdatedOn),
            $"$.in_network[{row.ItemIndex}].negotiated_rates[{row.RateIndex}].negotiated_prices[{row.PriceIndex}]",
            row.BillingCodeType,
            row.BillingCode,
            row.BillingCodePublished,
            row.CodeType,
            row.Code,
            row.CodeSource,
            row.AdditionalInformation,
            row.NegotiationArrangement,
            row.NegotiatedType,
            row.Rate,
            row.ExpirationDate,
            row.BillingClass,
            row.Setting,
            (row.Places ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(pos => new PlaceRef(pos, catalog.PlaceName(pos))).ToList(),
            row.Modifiers.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(code => new ModifierRef(code, catalog.Modifier(code).Label)).ToList(),
            row.Occurrences,
            row.ItemLabelCount,
            (row.ItemLabels ?? "").Split(" | ", StringSplitOptions.RemoveEmptyEntries),
            groups.Select(group => new RateSourceProviderGroup(
                group.ProviderGroupId,
                group.GroupIndex,
                TaxIdDisplay(group.Ein, group.EinUnverified, group.TinType, group.TinValue),
                group.EinUnverified,
                group.BusinessName,
                group.NpiCount,
                group.EinLevel,
                group.NetworkNames,
                group.ProviderMatch)).ToList(),
            (int)groupCount);
    }

    internal static string TaxIdDisplay(string? ein, bool unverified, string? tinType, string? tinValue) => ein switch
    {
        null => $"{(tinType ?? "tax id").ToUpperInvariant()} {tinValue}",
        _ when unverified => Ein.Mask(ein),
        _ => Ein.Format(ein),
    };

    private sealed record SourceRow(
        long RateId, string BillingCodeType, string BillingCode, string BillingCodePublished, string CodeType, string Code,
        string CodeSource, string? AdditionalInformation, string NegotiationArrangement, string NegotiatedType, decimal Rate,
        DateOnly? ExpirationDate, string BillingClass, string Setting, string Modifiers, int Occurrences, int ItemLabelCount,
        string? ItemLabels, int ItemIndex, int RateIndex, int PriceIndex, string? Places, int FileId, string ProductKey,
        string ProductLabel, string FileUrl, string? Sha256, DateOnly? FileLastUpdatedOn, DateTime? HttpLastModified,
        long? ContentLength, string? ReportingEntityName, string? SchemaVersion, int SnapshotId, string IndexUrl,
        DateOnly? IndexDate, DateOnly? IndexLastUpdatedOn);

    private sealed record GroupRow(
        int ProviderGroupId, int GroupIndex, string? TinType, string? TinValue, string? Ein, bool EinUnverified,
        string? BusinessName, int NpiCount, bool EinLevel, string? NetworkNames, bool ProviderMatch);
}

public sealed class ListRateNpisHandler(DuckDbConnectionFactory connections) : IRequestHandler<ListRateNpisQuery, RateNpisPage>
{
    public async Task<RateNpisPage> Handle(ListRateNpisQuery request, CancellationToken cancellationToken)
    {
        var provider = ProviderKeys.NormalizeKey(request.Provider);

        await using var connection = await connections.OpenAsync(cancellationToken);
        var exists = await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM rates WHERE rate_id = $rateId", new { rateId = request.RateId }) > 0;
        if (!exists)
        {
            throw new NotFoundException($"Rate {request.RateId} doesn't exist.", "rate_not_found");
        }

        var rateIds = new[] { request.RateId }.Concat(request.AlsoRateIds ?? []).Distinct().ToList();
        var total = await connection.ExecuteScalarAsync<long>(
            SqlFile.For<ListRateNpisHandler>("RateNpisCount.sql"), new { rateIds, provider });
        var rows = await connection.QueryAsync<NpiRow>(
            SqlFile.For<ListRateNpisHandler>("RateNpis.sql"),
            new { rateIds, provider, limit = request.PageSize, offset = (long)(request.Page - 1) * request.PageSize });

        return new RateNpisPage(
            request.RateId,
            provider,
            (int)total,
            request.Page,
            request.PageSize,
            rows.Select(row => new ListedNpi(row.Npi, row.Valid, row.Rate, row.RateHigh)).ToList());
    }

    private sealed record NpiRow(long Npi, bool Valid, decimal Rate, decimal RateHigh);
}

public sealed class SuggestCodesHandler(DuckDbConnectionFactory connections) : IRequestHandler<SuggestCodesQuery, IReadOnlyList<CodeSuggestion>>
{
    public async Task<IReadOnlyList<CodeSuggestion>> Handle(SuggestCodesQuery request, CancellationToken cancellationToken)
    {
        var text = request.Q!.Trim();
        var code = CodeInput.Clean(text);
        var digits = code.All(char.IsAsciiDigit) ? code.TrimStart('0') : "";

        await using var connection = await connections.OpenAsync(cancellationToken);
        var snapshot = await SnapshotResolver.ResolveAsync(connection, request.Snapshot);
        var rows = await connection.QueryAsync<SuggestionRow>(
            SqlFile.For<SuggestCodesHandler>("SuggestCodes.sql"),
            new { snapshotId = snapshot.SnapshotId, code, digits, text = text.ToLowerInvariant(), limit = request.Limit });

        return rows.Select(row =>
        {
            var category = ServiceCategories.Classify(row.CodeType, row.Code);
            return new CodeSuggestion(
                row.CodeType, row.Code, row.Name, row.Description, category.Label, category.Unit, row.Direct,
                (row.RevenueCodes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToList(),
                row.Rates);
        }).ToList();
    }

    private sealed record SuggestionRow(string CodeType, string Code, string? Name, string? Description, long Rates, bool Direct, string? RevenueCodes);
}

public sealed class SuggestProvidersHandler(DuckDbConnectionFactory connections) : IRequestHandler<SuggestProvidersQuery, IReadOnlyList<ProviderSuggestion>>
{
    public async Task<IReadOnlyList<ProviderSuggestion>> Handle(SuggestProvidersQuery request, CancellationToken cancellationToken)
    {
        var text = request.Q!.Trim();
        var digits = Npi.Digits(text);

        await using var connection = await connections.OpenAsync(cancellationToken);
        var snapshot = await SnapshotResolver.ResolveAsync(connection, request.Snapshot);
        var rows = await connection.QueryAsync<ProviderRow>(
            SqlFile.For<SuggestProvidersHandler>("SuggestProviders.sql"),
            new { snapshotId = snapshot.SnapshotId, text = text.ToLowerInvariant(), digits = digits.Length == 9 ? digits : "", limit = request.Limit });

        return rows.Select(row =>
        {
            var names = (row.BusinessNames ?? "").Split(" | ", StringSplitOptions.RemoveEmptyEntries);
            return new ProviderSuggestion(
                row.ProviderKey,
                GetRateSourceHandler.TaxIdDisplay(row.Ein, row.EinUnverified, null, row.ProviderKey),
                row.EinUnverified,
                names.FirstOrDefault() ?? "Name not published",
                names.Skip(1).ToList(),
                row.Rates);
        }).ToList();
    }

    private sealed record ProviderRow(string ProviderKey, string? Ein, bool EinUnverified, string? BusinessNames, long Rates);
}

public sealed class GetReferenceListsHandler(DuckDbConnectionFactory connections, ReferenceCatalog catalog, ProviderDirectoryInfo directory)
    : IRequestHandler<GetReferenceListsQuery, ReferenceLists>
{
    public async Task<ReferenceLists> Handle(GetReferenceListsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<LocationOption> locations = [];
        if (directory.IsLoaded)
        {
            await using var connection = await connections.OpenAsync(cancellationToken);
            locations = (await connection.QueryAsync<LocationOption>(SqlFile.For<GetReferenceListsHandler>("Locations.sql"))).ToList();
        }

        return new ReferenceLists(
            catalog.Settings.Select(setting => new SettingOption(setting.Key, setting.Label, setting.Note)).ToList(),
            SearchRatesQueryValidator.Sorts,
            locations,
            directory.Source);
    }
}

public sealed class SuggestSpecialtiesHandler(DuckDbConnectionFactory connections)
    : IRequestHandler<SuggestSpecialtiesQuery, IReadOnlyList<SpecialtySuggestion>>
{
    public async Task<IReadOnlyList<SpecialtySuggestion>> Handle(SuggestSpecialtiesQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return (await connection.QueryAsync<SpecialtySuggestion>(
            SqlFile.For<SuggestSpecialtiesHandler>("SuggestSpecialties.sql"),
            new { text = request.Q!.Trim(), limit = request.Limit })).ToList();
    }
}
