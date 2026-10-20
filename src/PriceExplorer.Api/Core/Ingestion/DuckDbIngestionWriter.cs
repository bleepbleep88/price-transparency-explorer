using System.Data.Common;
using System.Globalization;
using Dapper;
using DuckDB.NET.Data;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Domain;

namespace PriceExplorer.Api.Core.Ingestion;

/// <summary>Writes parsed files into DuckDB. Each file is replaced inside one transaction, so readers never see half a file.</summary>
internal sealed class DuckDbIngestionWriter(DuckDbConnectionFactory connections, TimeProvider time, ILogger<DuckDbIngestionWriter> logger)
{
    /// <summary>Bump when Schema.sql changes: all tables are rebuilt from the download cache.</summary>
    public const int SchemaVersion = 3;

    /// <summary>The meta key holding the per-install secret behind masked provider keys (see <see cref="ProviderKeys"/>).</summary>
    private const string ProviderKeySecret = "provider_key_secret";

    /// <summary>Bump when normalization rules change: every file is re-parsed even if its bytes didn't change.</summary>
    public const int ParserVersion = 1;

    /// <summary>Timestamps are stored as UTC. DuckDB's now() would write the server's local time without an offset.</summary>
    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    private static readonly string[] Tables =
    [
        "ingestion_rejects", "code_descriptions", "rate_provider_refs", "rate_places", "rates",
        "group_npis", "provider_groups", "source_files", "structure_files", "plans", "snapshots", "meta",
    ];

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var hasMeta = await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM information_schema.tables WHERE table_name = 'meta'") > 0;
        var version = hasMeta
            ? await connection.ExecuteScalarAsync<string?>("SELECT value FROM meta WHERE key = 'schema_version'")
            : null;

        // The secret outlives table rebuilds, so masked provider keys (and links that carry them) stay the same.
        var secret = hasMeta
            ? await connection.ExecuteScalarAsync<string?>("SELECT value FROM meta WHERE key = $key", new { key = ProviderKeySecret })
            : null;

        if (version == SchemaVersion.ToString(CultureInfo.InvariantCulture) && secret is not null)
        {
            return;
        }

        if (version is not null)
        {
            logger.LogInformation("Database schema {Old} → {New}: rebuilding tables from the download cache", version, SchemaVersion);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var table in Tables)
        {
            await connection.ExecuteAsync($"DROP TABLE IF EXISTS {table}", transaction: transaction);
        }

        await connection.ExecuteAsync(SqlFile.For<DuckDbConnectionFactory>("Schema.sql"), transaction: transaction);
        await connection.ExecuteAsync(
            "INSERT INTO meta (key, value) VALUES ('schema_version', $version), ($key, $secret)",
            new
            {
                version = SchemaVersion.ToString(CultureInfo.InvariantCulture),
                key = ProviderKeySecret,
                secret = secret ?? Convert.ToBase64String(ProviderKeys.NewSecret()),
            },
            transaction);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> HasLoadedDataAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM source_files WHERE status IN ('loaded', 'partial')") > 0;
    }

    public async Task<int> UpsertSnapshotAsync(string indexUrl, SnapshotHeader header, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var id = await connection.ExecuteScalarAsync<int?>("SELECT snapshot_id FROM snapshots WHERE index_url = $indexUrl", new { indexUrl })
            ?? await connection.ExecuteScalarAsync<int>("SELECT coalesce(max(snapshot_id), 0) + 1 FROM snapshots");

        await connection.ExecuteAsync(
            """
            INSERT INTO snapshots
                (snapshot_id, index_url, index_date, index_last_updated_on, reporting_entity_name, reporting_entity_type,
                 schema_version, index_sha256, status, error, updated_at)
            VALUES ($id, $indexUrl, $indexDate, $lastUpdatedOn, $entityName, $entityType, $schemaVersion, $sha256, 'loading', NULL, $now)
            ON CONFLICT (snapshot_id) DO UPDATE SET
                index_date = excluded.index_date,
                index_last_updated_on = excluded.index_last_updated_on,
                reporting_entity_name = excluded.reporting_entity_name,
                reporting_entity_type = excluded.reporting_entity_type,
                schema_version = excluded.schema_version,
                index_sha256 = excluded.index_sha256,
                updated_at = excluded.updated_at
            """,
            new
            {
                id,
                indexUrl,
                indexDate = header.IndexDate,
                lastUpdatedOn = header.LastUpdatedOn,
                entityName = header.ReportingEntityName,
                entityType = header.ReportingEntityType,
                schemaVersion = header.SchemaVersion,
                sha256 = header.Sha256,
                now = UtcNow,
            });
        return id;
    }

    public async Task ReplacePlansAsync(
        int snapshotId, IReadOnlyList<PlanRecord> plans, IReadOnlyList<StructureFileRecord> structureFiles, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync("DELETE FROM plans WHERE snapshot_id = $snapshotId", new { snapshotId }, transaction);
        await connection.ExecuteAsync("DELETE FROM structure_files WHERE snapshot_id = $snapshotId", new { snapshotId }, transaction);
        using (var appender = connection.CreateAppender("structure_files"))
        {
            foreach (var link in structureFiles)
            {
                appender.CreateRow().AppendValue(snapshotId).AppendValue(link.StructureIndex).AppendValue(link.Url).EndRow();
            }
        }

        using (var appender = connection.CreateAppender("plans"))
        {
            foreach (var plan in plans)
            {
                appender.CreateRow()
                    .AppendValue(snapshotId)
                    .AppendValue(plan.StructureIndex)
                    .AppendText(plan.PlanName)
                    .AppendText(plan.PlanIdType)
                    .AppendText(plan.PlanId)
                    .AppendText(plan.PlanMarketType)
                    .AppendText(plan.IssuerName)
                    .AppendText(plan.PlanSponsorName)
                    .EndRow();
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SourceFileState> UpsertSourceFileAsync(int snapshotId, SourceFileSpec spec, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var existing = await connection.QuerySingleOrDefaultAsync<SourceFileState>(
            """
            SELECT file_id AS FileId, status AS Status, sha256 AS Sha256, parser_version AS ParserVersion
            FROM source_files WHERE snapshot_id = $snapshotId AND kind = $kind AND url = $url
            """,
            new { snapshotId, kind = spec.Kind, url = spec.Url });

        if (existing is not null)
        {
            await connection.ExecuteAsync(
                """
                UPDATE source_files
                SET structure_index = $structureIndex, product_key = $productKey, product_label = $productLabel, description = $description
                WHERE file_id = $fileId
                """,
                new { fileId = existing.FileId, structureIndex = spec.StructureIndex, productKey = spec.ProductKey, productLabel = spec.ProductLabel, description = spec.Description });
            return existing;
        }

        var fileId = await connection.ExecuteScalarAsync<int>("SELECT coalesce(max(file_id), 0) + 1 FROM source_files");
        await connection.ExecuteAsync(
            """
            INSERT INTO source_files (file_id, snapshot_id, kind, structure_index, product_key, product_label, url, description, status)
            VALUES ($fileId, $snapshotId, $kind, $structureIndex, $productKey, $productLabel, $url, $description, 'pending')
            """,
            new { fileId, snapshotId, kind = spec.Kind, structureIndex = spec.StructureIndex, productKey = spec.ProductKey, productLabel = spec.ProductLabel, url = spec.Url, description = spec.Description });
        return new SourceFileState(fileId, "pending", null, null);
    }

    public async Task WriteInNetworkFileAsync(
        int fileId, InNetworkFileBuilder builder, DownloadedFile download, ParseOutcome outcome, TimeSpan parseTime,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var secret = Convert.FromBase64String(await connection.ExecuteScalarAsync<string>(
            "SELECT value FROM meta WHERE key = $key", new { key = ProviderKeySecret }, transaction)
            ?? throw new InvalidOperationException("The database has no provider key secret; EnsureSchemaAsync runs first."));

        await DeleteFileRowsAsync(connection, fileId, transaction);
        AppendProviderGroups(connection, fileId, builder, secret);
        AppendRates(connection, fileId, builder);
        AppendDescriptions(connection, fileId, builder);
        AppendRejects(connection, fileId, builder);

        var stats = builder.GetStats();
        await connection.ExecuteAsync(
            """
            UPDATE source_files SET
                status = $status, error = $error, warning = $warning, parser_version = $parserVersion,
                sha256 = $sha256, etag = $etag, http_last_modified = $lastModified, content_length = $length,
                file_last_updated_on = $fileLastUpdatedOn, file_schema_version = $schemaVersion, reporting_entity_name = $entityName,
                items = $items, prices_raw = $pricesRaw, rates_unique = $ratesUnique, duplicates_removed = $duplicatesRemoved,
                provider_references = $providerReferences, provider_groups = $providerGroups, distinct_npis = $distinctNpis,
                distinct_eins = $distinctEins, invalid_npis = $invalidNpis, ein_level_groups = $einLevelGroups,
                unverified_ein_groups = $unverifiedEinGroups, dangling_references = $danglingReferences,
                rejected_prices = $rejectedPrices, download_ms = $downloadMs, parse_ms = $parseMs, loaded_at = $now
            WHERE file_id = $fileId
            """,
            new
            {
                fileId,
                status = outcome.IsComplete ? "loaded" : "partial",
                error = outcome.Problem,
                warning = download.Warning,
                parserVersion = ParserVersion,
                sha256 = download.Sha256,
                etag = download.ETag,
                lastModified = download.LastModified?.UtcDateTime,
                length = download.Length,
                fileLastUpdatedOn = builder.LastUpdatedOn,
                schemaVersion = builder.SchemaVersion,
                entityName = builder.ReportingEntityName,
                items = stats.Items,
                pricesRaw = stats.PricesRaw,
                ratesUnique = stats.RatesUnique,
                duplicatesRemoved = stats.DuplicatesRemoved,
                providerReferences = stats.ProviderReferences,
                providerGroups = stats.ProviderGroups,
                distinctNpis = stats.DistinctNpis,
                distinctEins = stats.DistinctEins,
                invalidNpis = stats.InvalidNpis,
                einLevelGroups = stats.EinLevelGroups,
                unverifiedEinGroups = stats.UnverifiedEinGroups,
                danglingReferences = stats.DanglingReferences,
                rejectedPrices = stats.RejectedPrices,
                downloadMs = (int)download.Elapsed.TotalMilliseconds,
                parseMs = (int)parseTime.TotalMilliseconds,
                now = UtcNow,
            },
            transaction);

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>The bytes didn't change since the last load; only refresh what the payer's server told us.</summary>
    public async Task MarkFileUnchangedAsync(int fileId, DownloadedFile download, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            "UPDATE source_files SET warning = $warning, etag = coalesce($etag, etag) WHERE file_id = $fileId",
            new { fileId, warning = download.Warning, etag = download.ETag });
    }

    /// <summary>Records a failure. Data from an earlier successful load (if any) stays queryable.</summary>
    public async Task MarkFileFailedAsync(int fileId, string error, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            """
            UPDATE source_files
            SET status = CASE WHEN status IN ('loaded', 'partial') THEN status ELSE 'failed' END,
                error = CASE WHEN status IN ('loaded', 'partial') THEN error ELSE $error END,
                warning = CASE WHEN status IN ('loaded', 'partial') THEN $staleWarning ELSE warning END
            WHERE file_id = $fileId
            """,
            new { fileId, error, staleWarning = $"Showing the last good copy; refresh failed: {error}" });
    }

    public async Task<string> CompleteSnapshotAsync(int snapshotId, string? error, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var status = await connection.ExecuteScalarAsync<string>(
            """
            SELECT CASE
                WHEN count(*) = 0 THEN 'failed'
                WHEN count(*) FILTER (WHERE status = 'loaded') = count(*) THEN 'ready'
                WHEN count(*) FILTER (WHERE status IN ('loaded', 'partial')) > 0 THEN 'partial'
                ELSE 'failed' END
            FROM source_files WHERE snapshot_id = $snapshotId AND kind = 'in_network'
            """,
            new { snapshotId }) ?? "failed";

        await connection.ExecuteAsync(
            "UPDATE snapshots SET status = $status, error = $error, updated_at = $now WHERE snapshot_id = $snapshotId",
            new { snapshotId, status, error, now = UtcNow });
        return status;
    }

    private static async Task DeleteFileRowsAsync(DuckDBConnection connection, int fileId, DbTransaction transaction)
    {
        var args = new { fileId };
        await connection.ExecuteAsync("DELETE FROM rate_places WHERE (rate_id >> 32) = $fileId", args, transaction);
        await connection.ExecuteAsync("DELETE FROM rate_provider_refs WHERE file_id = $fileId", args, transaction);
        await connection.ExecuteAsync("DELETE FROM rates WHERE file_id = $fileId", args, transaction);
        await connection.ExecuteAsync("DELETE FROM group_npis WHERE file_id = $fileId", args, transaction);
        await connection.ExecuteAsync("DELETE FROM provider_groups WHERE file_id = $fileId", args, transaction);
        await connection.ExecuteAsync("DELETE FROM code_descriptions WHERE file_id = $fileId", args, transaction);
        await connection.ExecuteAsync("DELETE FROM ingestion_rejects WHERE file_id = $fileId", args, transaction);
    }

    private static void AppendProviderGroups(DuckDBConnection connection, int fileId, InNetworkFileBuilder builder, byte[] secret)
    {
        using (var appender = connection.CreateAppender("provider_groups"))
        {
            foreach (var group in builder.Groups)
            {
                appender.CreateRow()
                    .AppendValue(fileId)
                    .AppendValue(group.ProviderReferenceId)
                    .AppendValue(group.GroupIndex)
                    .AppendText(group.TinType)
                    .AppendText(group.TinValue)
                    .AppendText(group.Ein)
                    .AppendText(group.BusinessName)
                    .AppendValue(group.NpiCount)
                    .AppendValue(group.EinLevel)
                    .AppendValue(group.EinUnverified)
                    .AppendText(group.NetworkNames)
                    .AppendText(ProviderKeys.For(group.TinType, group.TinValue, group.Ein, group.EinUnverified, secret))
                    .EndRow();
            }
        }

        using var npis = connection.CreateAppender("group_npis");
        foreach (var npi in builder.GroupNpis)
        {
            npis.CreateRow()
                .AppendValue(fileId)
                .AppendValue(npi.ProviderReferenceId)
                .AppendValue(npi.GroupIndex)
                .AppendValue(npi.Npi)
                .AppendValue(npi.Valid)
                .EndRow();
        }
    }

    private static void AppendRates(DuckDBConnection connection, int fileId, InNetworkFileBuilder builder)
    {
        using var rates = connection.CreateAppender("rates");
        using var places = connection.CreateAppender("rate_places");
        using var references = connection.CreateAppender("rate_provider_refs");

        var sequence = 0L;
        foreach (var entry in builder.Rates)
        {
            var rateId = ((long)fileId << 32) | sequence++;
            var key = entry.Key;

            rates.CreateRow()
                .AppendValue(rateId)
                .AppendValue(fileId)
                .AppendValue(key.BillingCodeType)
                .AppendValue(key.BillingCode)
                .AppendValue(entry.BillingCodeRaw)
                .AppendValue(key.CodeType)
                .AppendValue(key.Code)
                .AppendValue(entry.CodeSource)
                .AppendValue(key.Arrangement)
                .AppendValue(key.NegotiatedType)
                .AppendValue(key.BillingClass)
                .AppendValue(key.Setting)
                .AppendValue(key.Modifiers)
                .AppendValue(key.Rate)
                .AppendDate(key.Expiration)
                .AppendText(key.AdditionalInformation.Length == 0 ? null : key.AdditionalInformation)
                .AppendValue(entry.Occurrences)
                .AppendValue(entry.LabelCount)
                .AppendText(entry.LabelSamples.Count == 0 ? null : string.Join(" | ", entry.LabelSamples))
                .AppendValue(entry.ItemIndex)
                .AppendValue(entry.RateIndex)
                .AppendValue(entry.PriceIndex)
                .EndRow();

            if (entry.Places.Length == 0)
            {
                places.CreateRow().AppendValue(rateId).AppendValue("").EndRow();
            }

            foreach (var place in entry.Places)
            {
                places.CreateRow().AppendValue(rateId).AppendValue(place).EndRow();
            }

            foreach (var reference in entry.ProviderReferences)
            {
                references.CreateRow().AppendValue(rateId).AppendValue(fileId).AppendValue(reference).EndRow();
            }
        }
    }

    private static void AppendDescriptions(DuckDBConnection connection, int fileId, InNetworkFileBuilder builder)
    {
        using var appender = connection.CreateAppender("code_descriptions");
        foreach (var description in builder.Descriptions)
        {
            appender.CreateRow()
                .AppendValue(fileId)
                .AppendValue(description.CodeType)
                .AppendValue(description.Code)
                .AppendText(description.Name)
                .AppendText(description.Description)
                .EndRow();
        }
    }

    private static void AppendRejects(DuckDBConnection connection, int fileId, InNetworkFileBuilder builder)
    {
        using var appender = connection.CreateAppender("ingestion_rejects");
        foreach (var reject in builder.Rejects)
        {
            appender.CreateRow()
                .AppendValue(fileId)
                .AppendValue(reject.Reason)
                .AppendValue(reject.Count)
                .AppendText(reject.Example)
                .EndRow();
        }
    }
}

internal static class AppenderRowExtensions
{
    public static IDuckDBAppenderRow AppendText(this IDuckDBAppenderRow row, string? value) =>
        value is null ? row.AppendNullValue() : row.AppendValue(value);

    public static IDuckDBAppenderRow AppendDate(this IDuckDBAppenderRow row, DateOnly? value) =>
        value is { } date ? row.AppendValue(date) : row.AppendNullValue();
}

internal sealed record SnapshotHeader(
    DateOnly? IndexDate,
    DateOnly? LastUpdatedOn,
    string? ReportingEntityName,
    string? ReportingEntityType,
    string? SchemaVersion,
    string Sha256);

internal sealed record PlanRecord(
    int StructureIndex,
    string? PlanName,
    string? PlanIdType,
    string? PlanId,
    string? PlanMarketType,
    string? IssuerName,
    string? PlanSponsorName);

internal sealed record SourceFileSpec(string Kind, int StructureIndex, string ProductKey, string ProductLabel, string Url, string? Description);

internal sealed record SourceFileState(int FileId, string Status, string? Sha256, int? ParserVersion);
