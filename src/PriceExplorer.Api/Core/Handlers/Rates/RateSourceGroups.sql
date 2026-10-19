SELECT
    pg.provider_ref_id AS ProviderGroupId,
    pg.group_idx       AS GroupIndex,
    pg.tin_type        AS TinType,
    pg.tin_value       AS TinValue,
    pg.ein             AS Ein,
    pg.ein_unverified  AS EinUnverified,
    pg.business_name   AS BusinessName,
    pg.npi_count       AS NpiCount,
    pg.ein_level       AS EinLevel,
    pg.network_names   AS NetworkNames
FROM rate_provider_refs rr
JOIN provider_groups pg ON pg.file_id = rr.file_id AND pg.provider_ref_id = rr.provider_ref_id
WHERE rr.rate_id = $rateId
ORDER BY pg.provider_ref_id, pg.group_idx
LIMIT 200
