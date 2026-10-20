-- Comparison groups with full statistics (10+ providers, so they have a gap to the lower quartile and a spread) from a
-- snapshot's saved statistics, under the filters SQL can apply. $codes: stored forms joined with ','. The handler adds
-- each group's category, ranks and pages. Column order matches GapGroupRow.
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
    g.unit               AS Unit,
    g.per_unit           AS PerUnit,
    g.provider_count     AS ProviderCount,
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
  AND g.stats_level = 'full'
  AND ($product::VARCHAR IS NULL OR g.product_key = $product::VARCHAR)
  AND ($setting::VARCHAR IS NULL OR g.setting_key = $setting::VARCHAR)
  AND ($feeType::VARCHAR IS NULL OR g.fee_type = $feeType::VARCHAR)
  AND ($codes::VARCHAR IS NULL OR list_contains(string_split($codes::VARCHAR, ','), g.code))
  AND ($codeType::VARCHAR IS NULL OR g.code_type = $codeType::VARCHAR)
ORDER BY g.group_key
