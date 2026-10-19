-- Where an NPI and/or EIN appears in this snapshot, and how many OTHER codes (not the searched ones) it has rates for.
-- Used to explain an empty provider-filtered search.
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
      AND ($ein::VARCHAR IS NULL OR pg.ein = $ein::VARCHAR
           OR (pg.ein_unverified AND 'u' || substr(sha256(pg.ein), 1, 12) = $ein::VARCHAR))
),
codes AS (
    SELECT r.code_type, r.code, count(*) AS rates
    FROM groups g
    JOIN rate_provider_refs rr ON rr.file_id = g.file_id AND rr.provider_ref_id = g.provider_ref_id
    JOIN rates r ON r.rate_id = rr.rate_id
    WHERE r.code NOT IN $codes
      AND (r.expiration_date IS NULL OR r.expiration_date >= current_date)
    GROUP BY r.code_type, r.code
)
SELECT
    (SELECT count(*) FROM groups)                                                   AS GroupCount,
    (SELECT string_agg(DISTINCT CASE WHEN ein_unverified THEN NULL ELSE ein END, ',') FROM groups) AS Eins,
    (SELECT bool_or(ein_unverified) FROM groups)                                    AS AnyUnverified,
    (SELECT string_agg(DISTINCT business_name, ' | ') FROM groups)                 AS BusinessNames,
    (SELECT count(*) FROM codes)                                                    AS CodeCount,
    (SELECT string_agg(code, ',' ORDER BY rates DESC, code) FROM (SELECT * FROM codes ORDER BY rates DESC, code LIMIT 5)) AS TopCodes
