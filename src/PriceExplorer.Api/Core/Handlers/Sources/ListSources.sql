-- Every snapshot and each data file it lists, newest first, rate files before allowed-amount files. Counts not stored on
-- source_files are computed from the saved rows: distinct masked EINs (prefixes the IRS never issues), distinct codes
-- whose published form lost its leading zeros, facility rates whose procedure code came from the notes field, and
-- EINs published as 8 digits that got their leading zero back.
WITH rate_counts AS (
    SELECT file_id,
           count(DISTINCT billing_code_type || ' ' || billing_code) FILTER (WHERE billing_code_raw <> billing_code) AS codes_restored,
           count(*) FILTER (WHERE code_source = 'additional_information') AS rates_from_notes
    FROM rates
    GROUP BY file_id
),
group_counts AS (
    SELECT file_id, count(DISTINCT ein) FILTER (WHERE ein_unverified) AS unverified_eins
    FROM provider_groups
    GROUP BY file_id
),
payment_counts AS (
    SELECT file_id,
           count(DISTINCT billing_code_type || ' ' || billing_code) FILTER (WHERE billing_code_raw <> billing_code) AS codes_restored,
           count(DISTINCT ein) FILTER (WHERE ein_unverified) AS unverified_eins,
           count(DISTINCT ein) FILTER (WHERE ein = '0' || tin_value) AS eins_restored
    FROM oon_payments
    GROUP BY file_id
)
SELECT
    s.snapshot_id                                          AS SnapshotId,
    s.index_date                                           AS IndexDate,
    s.index_url                                            AS IndexUrl,
    s.index_last_updated_on                                AS IndexLastUpdatedOn,
    s.index_sha256                                         AS IndexSha256,
    s.schema_version                                       AS SchemaVersion,
    s.reporting_entity_name                                AS ReportingEntityName,
    s.reporting_entity_type                                AS ReportingEntityType,
    s.status                                               AS SnapshotStatus,
    s.error                                                AS SnapshotError,
    s.updated_at                                           AS UpdatedAt,
    f.file_id                                              AS FileId,
    f.kind                                                 AS Kind,
    f.product_key                                          AS ProductKey,
    f.product_label                                        AS ProductLabel,
    f.url                                                  AS Url,
    f.description                                          AS Description,
    f.status                                               AS FileStatus,
    f.error                                                AS Error,
    f.warning                                              AS Warning,
    f.file_last_updated_on                                 AS FileLastUpdatedOn,
    f.http_last_modified                                   AS HttpLastModified,
    f.content_length                                       AS ContentLength,
    f.sha256                                               AS Sha256,
    f.etag                                                 AS ETag,
    f.file_schema_version                                  AS FileSchemaVersion,
    f.reporting_entity_name                                AS FileReportingEntityName,
    f.items                                                AS Items,
    f.prices_raw                                           AS Published,
    f.rates_unique                                         AS UniqueRows,
    f.duplicates_removed                                   AS DuplicatesRemoved,
    f.rejected_prices                                      AS Rejected,
    f.provider_groups                                      AS ProviderGroups,
    f.distinct_npis                                        AS DistinctNpis,
    f.distinct_eins                                        AS DistinctEins,
    f.invalid_npis                                         AS InvalidNpis,
    f.ein_level_groups                                     AS WholeTaxIdGroups,
    f.dangling_references                                  AS DanglingReferences,
    coalesce(gc.unverified_eins, pc.unverified_eins, 0)    AS UnverifiedEins,
    coalesce(rc.codes_restored, pc.codes_restored, 0)      AS CodesWithZerosRestored,
    rc.rates_from_notes                                    AS RatesFromNotes,
    pc.eins_restored                                       AS EinsWithZeroRestored,
    f.download_ms                                          AS DownloadMs,
    f.parse_ms                                             AS ParseMs,
    f.loaded_at                                            AS LoadedAt
FROM snapshots s
LEFT JOIN source_files f ON f.snapshot_id = s.snapshot_id
LEFT JOIN rate_counts rc ON rc.file_id = f.file_id
LEFT JOIN group_counts gc ON gc.file_id = f.file_id
LEFT JOIN payment_counts pc ON pc.file_id = f.file_id
ORDER BY s.index_date DESC NULLS LAST, s.snapshot_id DESC, f.kind = 'allowed_amount', f.structure_index, f.file_id
