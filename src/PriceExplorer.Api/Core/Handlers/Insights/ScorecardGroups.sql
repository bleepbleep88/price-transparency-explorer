-- Every comparison group a provider has a saved value in, with that value and the group's statistics: one row per
-- group. Column order matches ScorecardGroupRow.
SELECT
    v.group_key       AS GroupKey,
    g.product_key     AS ProductKey,
    g.product_label   AS ProductLabel,
    g.code_type       AS CodeType,
    g.code            AS Code,
    g.fee_type        AS FeeType,
    g.setting_key     AS SettingKey,
    g.modifiers       AS Modifiers,
    g.negotiated_type AS NegotiatedType,
    g.unit            AS Unit,
    g.per_unit        AS PerUnit,
    g.provider_count  AS ProviderCount,
    g.stats_level     AS StatsLevel,
    g.p25             AS P25,
    g.median          AS Median,
    g.p75             AS P75,
    v.value           AS Value,
    v.low             AS Low,
    v.high            AS High,
    v.business_name   AS BusinessName,
    v.ein             AS Ein,
    v.ein_display     AS EinDisplay,
    v.ein_unverified  AS EinMasked
FROM provider_values v
JOIN group_stats g ON g.snapshot_id = v.snapshot_id AND g.group_key = v.group_key
WHERE v.snapshot_id = $snapshotId AND v.provider_key = $provider
ORDER BY v.group_key
