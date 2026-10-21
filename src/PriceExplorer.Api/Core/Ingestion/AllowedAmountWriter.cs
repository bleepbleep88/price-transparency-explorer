using System.Data.Common;
using Dapper;
using DuckDB.NET.Data;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Domain;

namespace PriceExplorer.Api.Core.Ingestion;

/// <summary>source_files.kind values.</summary>
internal static class SourceKinds
{
    public const string InNetwork = "in_network";

    /// <summary>Out-of-network allowed amounts (the index's allowed_amount_file).</summary>
    public const string AllowedAmount = "allowed_amount";
}

/// <summary>
/// Saves a parsed allowed-amount file. Like rate files, each file is replaced inside one transaction, so readers never
/// see half a file, and tax IDs get the same provider keys as provider groups (masked ones under the install's secret).
/// </summary>
internal sealed class AllowedAmountWriter(DuckDbConnectionFactory connections, TimeProvider time)
{
    /// <summary>Bump when the allowed-amount clean-up rules change: every such file is re-parsed even if its bytes didn't.</summary>
    /// <remarks>2: item names and descriptions are kept only for HCPCS Level II codes (PublishedText).</remarks>
    public const int ParserVersion = 2;

    public async Task WriteAsync(
        int fileId, AllowedAmountFileBuilder builder, DownloadedFile download, ParseOutcome outcome, TimeSpan parseTime,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var secret = Convert.FromBase64String(await connection.ExecuteScalarAsync<string>(
            "SELECT value FROM meta WHERE key = $key", new { key = DuckDbIngestionWriter.ProviderKeySecret }, transaction)
            ?? throw new InvalidOperationException("The database has no provider key secret; EnsureSchemaAsync runs first."));

        await DeleteFileRowsAsync(connection, fileId, transaction);
        AppendItems(connection, fileId, builder);
        AppendPayments(connection, fileId, builder, secret);
        AppendRejects(connection, fileId, builder);

        var stats = builder.GetStats();
        await connection.ExecuteAsync(
            """
            UPDATE source_files SET
                status = $status, error = $error, warning = $warning, parser_version = $parserVersion,
                sha256 = $sha256, etag = $etag, http_last_modified = $lastModified, content_length = $length,
                file_last_updated_on = $fileLastUpdatedOn, file_schema_version = $schemaVersion, reporting_entity_name = $entityName,
                items = $items, prices_raw = $paymentsRaw, rates_unique = $paymentsUnique, duplicates_removed = $duplicatesRemoved,
                provider_references = NULL, provider_groups = $taxIdEntries, distinct_npis = $distinctNpis,
                distinct_eins = $distinctEins, invalid_npis = $invalidNpis, ein_level_groups = NULL,
                unverified_ein_groups = $unverifiedTaxIdEntries, dangling_references = NULL,
                rejected_prices = $rejectedPayments, download_ms = $downloadMs, parse_ms = $parseMs, loaded_at = $now
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
                paymentsRaw = stats.PaymentsRaw,
                paymentsUnique = stats.PaymentsUnique,
                duplicatesRemoved = stats.DuplicatesRemoved,
                taxIdEntries = stats.TaxIdEntries,
                distinctNpis = stats.DistinctNpis,
                distinctEins = stats.DistinctEins,
                invalidNpis = stats.InvalidNpis,
                unverifiedTaxIdEntries = stats.UnverifiedTaxIdEntries,
                rejectedPayments = stats.RejectedPayments,
                downloadMs = (int)download.Elapsed.TotalMilliseconds,
                parseMs = (int)parseTime.TotalMilliseconds,
                now = time.GetUtcNow().UtcDateTime,
            },
            transaction);

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task DeleteFileRowsAsync(DuckDBConnection connection, int fileId, DbTransaction transaction)
    {
        var args = new { fileId };
        await connection.ExecuteAsync("DELETE FROM oon_providers WHERE (payment_id >> 32) = $fileId", args, transaction);
        await connection.ExecuteAsync("DELETE FROM oon_payments WHERE file_id = $fileId", args, transaction);
        await connection.ExecuteAsync("DELETE FROM oon_items WHERE file_id = $fileId", args, transaction);
        await connection.ExecuteAsync("DELETE FROM ingestion_rejects WHERE file_id = $fileId", args, transaction);
    }

    private static void AppendItems(DuckDBConnection connection, int fileId, AllowedAmountFileBuilder builder)
    {
        using var appender = connection.CreateAppender("oon_items");
        foreach (var item in builder.Items)
        {
            appender.CreateRow()
                .AppendValue(fileId)
                .AppendValue(item.ItemIndex)
                .AppendValue(item.BillingCodeType)
                .AppendValue(item.BillingCode)
                .AppendValue(item.BillingCodeRaw)
                .AppendText(item.Name)
                .AppendText(item.Description)
                .AppendValue(item.Payments)
                .EndRow();
        }
    }

    private static void AppendPayments(DuckDBConnection connection, int fileId, AllowedAmountFileBuilder builder, byte[] secret)
    {
        using var payments = connection.CreateAppender("oon_payments");
        using var providers = connection.CreateAppender("oon_providers");

        var sequence = 0L;
        foreach (var payment in builder.Payments)
        {
            var paymentId = ((long)fileId << 32) | sequence++;
            var key = payment.Key;
            var taxId = payment.TaxId;

            payments.CreateRow()
                .AppendValue(paymentId)
                .AppendValue(fileId)
                .AppendValue(key.BillingCodeType)
                .AppendValue(key.BillingCode)
                .AppendValue(payment.BillingCodeRaw)
                .AppendValue(taxId.Type)
                .AppendValue(taxId.Value)
                .AppendText(taxId.Ein)
                .AppendValue(taxId.EinUnverified)
                .AppendValue(ProviderKeys.For(taxId.Type, taxId.Value, taxId.Ein, taxId.EinUnverified, secret))
                .AppendValue(key.Places)
                .AppendValue(key.BillingClass)
                .AppendValue(key.Modifiers)
                .AppendValue(key.AllowedAmount)
                .AppendValue(payment.Occurrences)
                .AppendValue(payment.LabelCount)
                .AppendText(payment.LabelSamples.Count == 0 ? null : string.Join(" | ", payment.LabelSamples))
                .AppendValue(payment.Path.ItemIndex)
                .AppendValue(payment.Path.AllowedIndex)
                .AppendValue(payment.Path.PaymentIndex)
                .EndRow();

            foreach (var provider in payment.Providers)
            {
                if (provider.Npis.Count == 0)
                {
                    providers.CreateRow()
                        .AppendValue(paymentId).AppendValue(provider.ProviderIndex).AppendNumber(provider.BilledCharge)
                        .AppendNullValue().AppendNullValue()
                        .EndRow();
                }

                foreach (var npi in provider.Npis)
                {
                    providers.CreateRow()
                        .AppendValue(paymentId).AppendValue(provider.ProviderIndex).AppendNumber(provider.BilledCharge)
                        .AppendValue(npi.Npi).AppendValue(npi.Valid)
                        .EndRow();
                }
            }
        }
    }

    private static void AppendRejects(DuckDBConnection connection, int fileId, AllowedAmountFileBuilder builder)
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
