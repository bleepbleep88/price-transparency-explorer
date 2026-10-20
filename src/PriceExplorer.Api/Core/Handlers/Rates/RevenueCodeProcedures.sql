-- Facility rates filed under a searched revenue code that are searchable under the procedure code in their notes field
-- (additional_information). Searching the revenue code alone would never show them.
-- Scoped to one product when $product is set, and to a provider when NPI / EIN / name filters are set (NPI, EIN and
-- name in the same provider group, as in SearchRates.sql). One row per revenue code and procedure code; the last four
-- columns describe all rows together: Providers (distinct tax IDs; 0 without provider filters), Products (labels),
-- and Eins / BusinessNames of the matching groups (masked tax IDs left out).
WITH target_files AS (
    SELECT file_id, product_label
    FROM source_files
    WHERE snapshot_id = $snapshotId AND kind = 'in_network' AND status IN ('loaded', 'partial')
      AND ($product::VARCHAR IS NULL OR product_key = $product::VARCHAR)
),
refiled AS (
    SELECT r.rate_id, r.billing_code, r.code_type, r.code, tf.product_label
    FROM rates r
    JOIN target_files tf ON tf.file_id = r.file_id
    WHERE r.billing_code_type = 'RC'
      AND r.billing_code IN $codes
      AND r.code_source = 'additional_information'
      AND (r.expiration_date IS NULL OR r.expiration_date >= current_date)
),
hits AS (
    SELECT f.*, NULL::VARCHAR AS provider_key, NULL::VARCHAR AS ein, NULL::VARCHAR AS business_name
    FROM refiled f
    WHERE $npi::BIGINT IS NULL AND $ein::VARCHAR IS NULL AND $provider::VARCHAR IS NULL
    UNION ALL
    SELECT f.*, pg.provider_key, CASE WHEN pg.ein_unverified THEN NULL ELSE pg.ein END, pg.business_name
    FROM refiled f
    JOIN rate_provider_refs rr ON rr.rate_id = f.rate_id
    JOIN provider_groups pg ON pg.file_id = rr.file_id AND pg.provider_ref_id = rr.provider_ref_id
    WHERE NOT ($npi::BIGINT IS NULL AND $ein::VARCHAR IS NULL AND $provider::VARCHAR IS NULL)
      AND ($npi::BIGINT IS NULL OR EXISTS (
          SELECT 1 FROM group_npis gn
          WHERE gn.file_id = pg.file_id AND gn.provider_ref_id = pg.provider_ref_id
            AND gn.group_idx = pg.group_idx AND gn.npi = $npi::BIGINT))
      AND ($ein::VARCHAR IS NULL OR pg.provider_key = $ein::VARCHAR OR pg.ein = $ein::VARCHAR)
      AND ($provider::VARCHAR IS NULL OR contains(lower(coalesce(pg.business_name, '')), $provider::VARCHAR))
)
SELECT
    h.billing_code            AS RevenueCode,
    h.code_type               AS CodeType,
    h.code                    AS Code,
    count(DISTINCT h.rate_id) AS Rates,
    (SELECT count(DISTINCT provider_key) FROM hits)                                       AS Providers,
    (SELECT string_agg(DISTINCT product_label, ', ' ORDER BY product_label) FROM hits)    AS Products,
    (SELECT string_agg(DISTINCT ein, ',' ORDER BY ein) FROM hits)                         AS Eins,
    (SELECT string_agg(DISTINCT business_name, ' | ' ORDER BY business_name) FROM hits)   AS BusinessNames
FROM hits h
GROUP BY h.billing_code, h.code_type, h.code
ORDER BY Rates DESC, h.code
