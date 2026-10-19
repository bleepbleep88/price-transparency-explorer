-- NPIs listed for a rate, optionally only under one provider key (EIN, masked key, or "type:value" for non-EIN tax IDs).
SELECT
    gn.npi                 AS Npi,
    bool_and(gn.npi_valid) AS Valid,
    count(*) OVER ()       AS Total
FROM rate_provider_refs rr
JOIN provider_groups pg ON pg.file_id = rr.file_id AND pg.provider_ref_id = rr.provider_ref_id
JOIN group_npis gn ON gn.file_id = pg.file_id AND gn.provider_ref_id = pg.provider_ref_id AND gn.group_idx = pg.group_idx
WHERE rr.rate_id = $rateId
  AND ($provider::VARCHAR IS NULL
       OR pg.ein = $provider::VARCHAR
       OR (pg.ein_unverified AND 'u' || substr(sha256(pg.ein), 1, 12) = $provider::VARCHAR)
       OR (pg.ein IS NULL AND pg.tin_type || ':' || coalesce(pg.tin_value, '') = $provider::VARCHAR))
GROUP BY gn.npi
ORDER BY gn.npi
LIMIT $limit OFFSET $offset
