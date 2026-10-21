-- Who a provider (tax ID) is in a snapshot's rate files: its tax ID, every name on its provider groups that carry a
-- current rate (as shown: practice_name), whether any of those is a contract with the whole tax ID (npi [0]), and the distinct NPIs they list.
WITH target_files AS (
    SELECT file_id
    FROM source_files
    WHERE snapshot_id = $snapshotId AND kind = 'in_network' AND status IN ('loaded', 'partial')
),
groups AS (
    SELECT DISTINCT pg.file_id, pg.provider_ref_id, pg.group_idx, pg.ein, pg.ein_unverified, pg.ein_level,
           practice_name(pg.file_id, pg.provider_key, pg.business_name) AS business_name
    FROM provider_groups pg
    JOIN target_files tf ON tf.file_id = pg.file_id
    WHERE pg.provider_key = $provider
      AND EXISTS (
          SELECT 1
          FROM rate_provider_refs rr
          JOIN rates r ON r.rate_id = rr.rate_id
          WHERE rr.file_id = pg.file_id AND rr.provider_ref_id = pg.provider_ref_id
            AND (r.expiration_date IS NULL OR r.expiration_date >= current_date))
)
SELECT
    any_value(g.ein)                                                      AS Ein,
    coalesce(bool_or(g.ein_unverified), false)                            AS EinUnverified,
    coalesce(bool_or(g.ein_level), false)                                 AS WholeTaxId,
    string_agg(DISTINCT g.business_name, ' | ' ORDER BY g.business_name) AS Names,
    (SELECT count(DISTINCT gn.npi)
     FROM groups listed
     JOIN group_npis gn
       ON gn.file_id = listed.file_id AND gn.provider_ref_id = listed.provider_ref_id AND gn.group_idx = listed.group_idx) AS NpiCount
FROM groups g
