-- The same provider (tax ID) in the same comparison group under two products: every part of the group key but the
-- product matches (code type, code, fee type, setting, modifiers, rate type). One row per pair with both saved values
-- and both groups' units. $codes: stored forms joined with ','. Column order matches PairRow.
SELECT
    a.provider_key    AS ProviderKey,
    a.ein             AS Ein,
    a.ein_display     AS EinDisplay,
    a.ein_unverified  AS EinMasked,
    a.business_name   AS BusinessName,
    a.code_type       AS CodeType,
    a.code            AS Code,
    a.fee_type        AS FeeType,
    a.setting_key     AS SettingKey,
    a.modifiers       AS Modifiers,
    a.group_key       AS ProductGroupKey,
    b.group_key       AS VersusGroupKey,
    ga.unit           AS ProductUnit,
    gb.unit           AS VersusUnit,
    ga.per_unit       AS PerUnit,
    a.value           AS ProductValue,
    b.value           AS VersusValue
FROM provider_values a
JOIN provider_values b
  ON b.snapshot_id = a.snapshot_id AND b.provider_key = a.provider_key AND b.product_key = $versus
 AND b.code_type = a.code_type AND b.code = a.code AND b.fee_type = a.fee_type AND b.setting_key = a.setting_key
 AND b.modifiers = a.modifiers AND b.negotiated_type = a.negotiated_type
JOIN group_stats ga ON ga.snapshot_id = a.snapshot_id AND ga.group_key = a.group_key
JOIN group_stats gb ON gb.snapshot_id = b.snapshot_id AND gb.group_key = b.group_key
WHERE a.snapshot_id = $snapshotId
  AND a.product_key = $product
  AND ($codes::VARCHAR IS NULL OR list_contains(string_split($codes::VARCHAR, ','), a.code))
  AND ($codeType::VARCHAR IS NULL OR a.code_type = $codeType::VARCHAR)
  AND ($setting::VARCHAR IS NULL OR a.setting_key = $setting::VARCHAR)
ORDER BY a.code_type, a.code, a.group_key, a.provider_key
