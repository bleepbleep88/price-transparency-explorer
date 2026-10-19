-- Which kinds of code (CPT, HCPCS, RC...) these codes exist as in the snapshot: explains a search restricted to the wrong
-- type. A revenue code counts even when all its facility rates are filed under the procedure code in their notes field.
WITH target_files AS (
    SELECT file_id
    FROM source_files
    WHERE snapshot_id = $snapshotId AND kind = 'in_network' AND status IN ('loaded', 'partial')
)
SELECT DISTINCT r.code_type AS CodeType, r.code AS Code
FROM rates r
JOIN target_files tf ON tf.file_id = r.file_id
WHERE r.code IN $codes
  AND (r.expiration_date IS NULL OR r.expiration_date >= current_date)
UNION
SELECT DISTINCT r.billing_code_type, r.billing_code
FROM rates r
JOIN target_files tf ON tf.file_id = r.file_id
WHERE r.billing_code_type = 'RC'
  AND r.billing_code IN $codes
  AND (r.expiration_date IS NULL OR r.expiration_date >= current_date)
ORDER BY CodeType, Code
