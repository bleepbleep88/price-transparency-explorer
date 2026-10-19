-- Facility rates filed under a searched revenue code that are searchable under the procedure code in their notes field
-- (additional_information). Searching the revenue code alone would never show them.
-- Scoped like the search: to one product, and to a provider when NPI / EIN / name filters are set (NPI and EIN in the
-- same provider group, as in SearchRates.sql).
WITH target_files AS (
    SELECT file_id
    FROM source_files
    WHERE snapshot_id = $snapshotId AND kind = 'in_network' AND status IN ('loaded', 'partial')
      AND ($product::VARCHAR IS NULL OR product_key = $product::VARCHAR)
)
SELECT
    r.billing_code AS RevenueCode,
    r.code_type    AS CodeType,
    r.code         AS Code,
    count(*)       AS Rates
FROM rates r
JOIN target_files tf ON tf.file_id = r.file_id
WHERE r.billing_code_type = 'RC'
  AND r.billing_code IN $codes
  AND r.code_source = 'additional_information'
  AND (r.expiration_date IS NULL OR r.expiration_date >= current_date)
  AND (($npi::BIGINT IS NULL AND $ein::VARCHAR IS NULL AND $provider::VARCHAR IS NULL) OR EXISTS (
      SELECT 1
      FROM rate_provider_refs rr
      JOIN provider_groups pg ON pg.file_id = rr.file_id AND pg.provider_ref_id = rr.provider_ref_id
      WHERE rr.rate_id = r.rate_id
        AND ($npi::BIGINT IS NULL OR EXISTS (
            SELECT 1 FROM group_npis gn
            WHERE gn.file_id = pg.file_id AND gn.provider_ref_id = pg.provider_ref_id
              AND gn.group_idx = pg.group_idx AND gn.npi = $npi::BIGINT))
        AND ($ein::VARCHAR IS NULL OR pg.ein = $ein::VARCHAR
             OR (pg.ein_unverified AND 'u' || substr(sha256(pg.ein), 1, 12) = $ein::VARCHAR))
        AND ($provider::VARCHAR IS NULL OR contains(lower(coalesce(pg.business_name, '')), $provider::VARCHAR))))
GROUP BY r.billing_code, r.code_type, r.code
ORDER BY Rates DESC, r.code
