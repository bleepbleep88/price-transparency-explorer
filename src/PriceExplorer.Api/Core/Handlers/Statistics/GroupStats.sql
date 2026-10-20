-- A snapshot's saved comparison groups (see SnapshotStatisticsBuilder), optionally only some codes ($codes: stored
-- forms joined with ',') or only groups where one provider has a value. Column order matches GroupRow.
SELECT
    g.group_key          AS GroupKey,
    g.product_key        AS ProductKey,
    g.product_label      AS ProductLabel,
    g.code_type          AS CodeType,
    g.code               AS Code,
    g.fee_type           AS FeeType,
    g.setting_key        AS SettingKey,
    g.modifiers          AS Modifiers,
    g.negotiated_type    AS NegotiatedType,
    g.provider_count     AS ProviderCount,
    g.rate_count         AS RateCount,
    g.stats_level        AS StatsLevel,
    g.min                AS Min,
    g.p10                AS P10,
    g.p25                AS P25,
    g.median             AS Median,
    g.p75                AS P75,
    g.p90                AS P90,
    g.max                AS Max,
    g.spread_multiple    AS SpreadMultiple,
    g.p90_to_p10         AS P90ToP10,
    g.lower_quartile_gap AS LowerQuartileGap
FROM group_stats g
WHERE g.snapshot_id = $snapshotId
  AND ($codes::VARCHAR IS NULL OR list_contains(string_split($codes::VARCHAR, ','), g.code))
  AND ($provider::VARCHAR IS NULL OR EXISTS (
      SELECT 1 FROM provider_values v
      WHERE v.snapshot_id = g.snapshot_id AND v.group_key = g.group_key AND v.provider_key = $provider::VARCHAR))
ORDER BY g.group_key
