-- Which kinds of code (CPT, HCPCS, RC...) these codes exist as in the snapshot: explains a search restricted to the wrong
-- type. One row per kind:
--   Searchable: some rate is stored under that code, so searching it finds rates. A revenue code whose facility rates
--   are all filed under the procedure code in their notes field is listed too, but isn't searchable.
--   ForProvider: with provider filters, one of those searchable rates has a provider group matching them all (NPI, EIN
--   and name in the same group, as in SearchRates.sql); always true without provider filters.
--   InProduct: one of those searchable rates is in the selected product (always true without one).
WITH target_files AS (
    SELECT file_id, product_key
    FROM source_files
    WHERE snapshot_id = $snapshotId AND kind = 'in_network' AND status IN ('loaded', 'partial')
),
kinds AS (
    SELECT r.rate_id, r.code_type, r.code, true AS searchable, tf.product_key
    FROM rates r
    JOIN target_files tf ON tf.file_id = r.file_id
    WHERE r.code IN $codes
      AND (r.expiration_date IS NULL OR r.expiration_date >= current_date)
    UNION ALL
    SELECT r.rate_id, r.billing_code_type, r.billing_code, false, tf.product_key
    FROM rates r
    JOIN target_files tf ON tf.file_id = r.file_id
    WHERE r.billing_code_type = 'RC'
      AND r.billing_code IN $codes
      AND r.code_source = 'additional_information'
      AND (r.expiration_date IS NULL OR r.expiration_date >= current_date)
)
SELECT
    k.code_type          AS CodeType,
    k.code               AS Code,
    bool_or(k.searchable) AS Searchable,
    bool_or(k.searchable AND (($npi::BIGINT IS NULL AND $ein::VARCHAR IS NULL AND $provider::VARCHAR IS NULL) OR EXISTS (
        SELECT 1
        FROM rate_provider_refs rr
        JOIN provider_groups pg ON pg.file_id = rr.file_id AND pg.provider_ref_id = rr.provider_ref_id
        WHERE rr.rate_id = k.rate_id
          AND ($npi::BIGINT IS NULL OR EXISTS (
              SELECT 1 FROM group_npis gn
              WHERE gn.file_id = pg.file_id AND gn.provider_ref_id = pg.provider_ref_id
                AND gn.group_idx = pg.group_idx AND gn.npi = $npi::BIGINT))
          AND ($ein::VARCHAR IS NULL OR pg.provider_key = $ein::VARCHAR OR pg.ein = $ein::VARCHAR)
          AND ($provider::VARCHAR IS NULL OR contains(lower(coalesce(pg.business_name, '')), $provider::VARCHAR))))) AS ForProvider,
    bool_or(k.searchable AND ($product::VARCHAR IS NULL OR k.product_key = $product::VARCHAR)) AS InProduct
FROM kinds k
GROUP BY k.code_type, k.code
ORDER BY CodeType, Code
