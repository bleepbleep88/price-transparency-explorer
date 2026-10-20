-- The provider groups a rate covers, first 200 (a few rates list hundreds; the handler reports the full count).
-- With a provider key, that provider's groups come first, so the row's own provider is always in the list.
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
    pg.network_names   AS NetworkNames,
    coalesce(pg.provider_key = $provider::VARCHAR, false) AS ProviderMatch
FROM rate_provider_refs rr
JOIN provider_groups pg ON pg.file_id = rr.file_id AND pg.provider_ref_id = rr.provider_ref_id
WHERE rr.rate_id = $rateId
ORDER BY ProviderMatch DESC, pg.provider_ref_id, pg.group_idx
LIMIT 200
