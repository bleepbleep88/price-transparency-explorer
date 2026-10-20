-- How many distinct NPIs RateNpis.sql lists in total (same filters), so every page reports the same total.
SELECT count(DISTINCT gn.npi)
FROM rate_provider_refs rr
JOIN provider_groups pg ON pg.file_id = rr.file_id AND pg.provider_ref_id = rr.provider_ref_id
JOIN group_npis gn ON gn.file_id = pg.file_id AND gn.provider_ref_id = pg.provider_ref_id AND gn.group_idx = pg.group_idx
WHERE rr.rate_id IN $rateIds
  AND ($provider::VARCHAR IS NULL OR pg.provider_key = $provider::VARCHAR OR pg.ein = $provider::VARCHAR)
