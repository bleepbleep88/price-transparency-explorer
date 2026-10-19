SELECT
    s.snapshot_id           AS SnapshotId,
    s.index_date            AS IndexDate,
    s.index_url             AS IndexUrl,
    s.index_last_updated_on AS IndexLastUpdatedOn,
    s.reporting_entity_name AS ReportingEntityName,
    s.reporting_entity_type AS ReportingEntityType,
    s.status                AS SnapshotStatus,
    f.file_id               AS FileId,
    f.product_key           AS ProductKey,
    f.product_label         AS ProductLabel,
    f.url                   AS Url,
    f.status                AS FileStatus,
    f.error                 AS Error,
    f.warning               AS Warning,
    f.file_last_updated_on  AS FileLastUpdatedOn,
    f.http_last_modified    AS HttpLastModified,
    f.content_length        AS ContentLength,
    f.sha256                AS Sha256,
    (SELECT count(*) FROM plans p
      JOIN structure_files sf ON sf.snapshot_id = p.snapshot_id AND sf.structure_index = p.structure_index
      WHERE sf.snapshot_id = s.snapshot_id AND sf.url = f.url)                                          AS PlanEntries,
    (SELECT count(DISTINCT p.plan_id) FROM plans p
      JOIN structure_files sf ON sf.snapshot_id = p.snapshot_id AND sf.structure_index = p.structure_index
      WHERE sf.snapshot_id = s.snapshot_id AND sf.url = f.url)                                          AS DistinctPlanIds,
    f.items                 AS Items,
    f.prices_raw            AS PricesPublished,
    f.rates_unique          AS UniqueRates,
    f.duplicates_removed    AS DuplicatesRemoved,
    f.distinct_npis         AS DistinctNpis,
    f.distinct_eins         AS DistinctEins,
    f.loaded_at             AS LoadedAt
FROM snapshots s
LEFT JOIN source_files f ON f.snapshot_id = s.snapshot_id AND f.kind = 'in_network'
ORDER BY s.index_date DESC NULLS LAST, s.snapshot_id DESC, f.structure_index
