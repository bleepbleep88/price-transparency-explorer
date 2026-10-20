-- Where an NPI and/or EIN appears in this snapshot, used to explain an empty provider-filtered search:
--   GroupCount: the provider groups it is listed in.
--   TaxIds: one record per tax ID it is listed under: name, EIN (empty when masked), 'm' when masked, count of other
--   names; fields split by chr(31), records by chr(30), in a fixed order. Descriptions are built from these, so a name
--   never sits next to another tax ID's EIN.
--   CodeCount / TopCodes: the OTHER codes (not the searched ones, under any code type) it has rates for, including
--   procedure codes behind facility rates filed under a searched revenue code.
--   WholeTinKeys / WholeTinNames: tax IDs it is listed under that also hold a contract for the searched code with the
--   whole tax ID (npi [0], NPIs not listed), which may cover this NPI.
WITH target_files AS (
    SELECT file_id
    FROM source_files
    WHERE snapshot_id = $snapshotId AND kind = 'in_network' AND status IN ('loaded', 'partial')
),
groups AS (
    SELECT pg.*
    FROM provider_groups pg
    JOIN target_files tf ON tf.file_id = pg.file_id
    WHERE ($npi::BIGINT IS NULL OR EXISTS (
              SELECT 1 FROM group_npis gn
              WHERE gn.file_id = pg.file_id AND gn.provider_ref_id = pg.provider_ref_id
                AND gn.group_idx = pg.group_idx AND gn.npi = $npi::BIGINT))
      AND ($ein::VARCHAR IS NULL OR pg.provider_key = $ein::VARCHAR OR pg.ein = $ein::VARCHAR)
),
codes AS (
    SELECT r.code_type, r.code, count(*) AS rates
    FROM groups g
    JOIN rate_provider_refs rr ON rr.file_id = g.file_id AND rr.provider_ref_id = g.provider_ref_id
    JOIN rates r ON r.rate_id = rr.rate_id
    WHERE r.code NOT IN $codes
      AND (r.expiration_date IS NULL OR r.expiration_date >= current_date)
    GROUP BY r.code_type, r.code
),
tax_ids AS (
    SELECT provider_key,
           any_value(CASE WHEN ein_unverified THEN NULL ELSE ein END) AS ein,
           bool_or(ein_unverified) AS masked,
           min(business_name) AS name,
           count(DISTINCT business_name) - 1 AS other_names
    FROM groups
    GROUP BY provider_key
),
whole_tin AS (
    SELECT DISTINCT
        pg.provider_key,
        pg.business_name
    FROM provider_groups pg
    JOIN target_files tf ON tf.file_id = pg.file_id
    JOIN rate_provider_refs rr ON rr.file_id = pg.file_id AND rr.provider_ref_id = pg.provider_ref_id
    JOIN rates r ON r.rate_id = rr.rate_id
    WHERE pg.ein_level
      AND pg.ein IN (SELECT ein FROM groups WHERE ein IS NOT NULL)
      AND r.code IN $codes
      AND (r.expiration_date IS NULL OR r.expiration_date >= current_date)
)
SELECT
    (SELECT count(*) FROM groups)                                                   AS GroupCount,
    (SELECT count(*) FROM codes)                                                    AS CodeCount,
    (SELECT string_agg(code, ',' ORDER BY rates DESC, code) FROM (SELECT * FROM codes ORDER BY rates DESC, code LIMIT 5)) AS TopCodes,
    (SELECT string_agg(DISTINCT provider_key, ',' ORDER BY provider_key) FROM whole_tin) AS WholeTinKeys,
    (SELECT string_agg(DISTINCT business_name, ' | ' ORDER BY business_name) FROM whole_tin) AS WholeTinNames,
    (SELECT string_agg(coalesce(name, '') || chr(31) || coalesce(ein, '') || chr(31) || CASE WHEN masked THEN 'm' ELSE '' END
                       || chr(31) || CAST(other_names AS VARCHAR), chr(30) ORDER BY name, provider_key) FROM tax_ids) AS TaxIds
