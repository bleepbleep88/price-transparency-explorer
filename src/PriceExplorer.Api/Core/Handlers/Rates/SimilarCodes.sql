-- Codes in this snapshot that start like the one searched for (suggestions when nothing matched), optionally of one code
-- type. With provider filters, only codes that provider has rates for (NPI, EIN and name in the same provider group, as in
-- SearchRates.sql).
WITH target_files AS (
    SELECT file_id
    FROM source_files
    WHERE snapshot_id = $snapshotId AND kind = 'in_network' AND status IN ('loaded', 'partial')
),
candidates AS (
    SELECT r.rate_id, r.file_id, r.code_type, r.code
    FROM rates r
    JOIN target_files tf ON tf.file_id = r.file_id
    WHERE starts_with(r.code, $prefix)
      AND ($codeType::VARCHAR IS NULL OR r.code_type = $codeType::VARCHAR)
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
            AND ($ein::VARCHAR IS NULL OR pg.provider_key = $ein::VARCHAR OR pg.ein = $ein::VARCHAR)
            AND ($provider::VARCHAR IS NULL OR contains(lower(coalesce(pg.business_name, '')), $provider::VARCHAR))))
)
SELECT
    c.code_type              AS CodeType,
    c.code                   AS Code,
    count(DISTINCT c.rate_id) AS Rates,
    any_value(cd.name)       AS Name
FROM candidates c
LEFT JOIN code_descriptions cd ON cd.file_id = c.file_id AND cd.code_type = c.code_type AND cd.code = c.code
GROUP BY c.code_type, c.code
ORDER BY Rates DESC, c.code
LIMIT 5
