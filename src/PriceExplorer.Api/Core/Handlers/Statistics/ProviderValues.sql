-- Each provider's saved value in the groups GroupStats.sql returns, with the same filters. Column order matches ValueRow.
SELECT
    v.group_key            AS GroupKey,
    v.provider_key         AS ProviderKey,
    v.ein                  AS Ein,
    v.ein_display          AS EinDisplay,
    v.ein_unverified       AS EinMasked,
    v.ein_level            AS EinLevel,
    v.business_name        AS BusinessName,
    v.value                AS Value,
    v.low                  AS Low,
    v.high                 AS High,
    v.rate_count           AS RateCount,
    v.npi_weight           AS NpiWeight,
    v.first_rate_id        AS FirstRateId,
    v.whole_tax_id_rate_id AS WholeTaxIdRateId
FROM provider_values v
WHERE v.snapshot_id = $snapshotId
  AND ($codes::VARCHAR IS NULL OR list_contains(string_split($codes::VARCHAR, ','), v.code))
  AND ($provider::VARCHAR IS NULL OR v.provider_key = $provider::VARCHAR)
ORDER BY v.group_key, v.provider_key
