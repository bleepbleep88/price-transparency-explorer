-- NPIs listed for one or more rates (a result row's rates), optionally only under one provider key (EIN, masked key,
-- or "type:value" for non-EIN tax IDs). Each NPI comes with the lowest and highest of those rates it is listed for.
SELECT
    gn.npi                 AS Npi,
    bool_and(gn.npi_valid) AS Valid,
    min(r.rate)            AS Rate,
    max(r.rate)            AS RateHigh
FROM rate_provider_refs rr
JOIN rates r ON r.rate_id = rr.rate_id
JOIN provider_groups pg ON pg.file_id = rr.file_id AND pg.provider_ref_id = rr.provider_ref_id
JOIN group_npis gn ON gn.file_id = pg.file_id AND gn.provider_ref_id = pg.provider_ref_id AND gn.group_idx = pg.group_idx
WHERE rr.rate_id IN $rateIds
  AND ($provider::VARCHAR IS NULL OR pg.provider_key = $provider::VARCHAR OR pg.ein = $provider::VARCHAR)
GROUP BY gn.npi
ORDER BY gn.npi
LIMIT $limit OFFSET $offset
