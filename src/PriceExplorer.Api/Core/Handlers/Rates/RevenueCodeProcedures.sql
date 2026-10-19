-- Facility rates filed under a searched revenue code that are searchable under the procedure code in their notes field
-- (additional_information). Searching the revenue code alone would never show them.
WITH target_files AS (
    SELECT file_id
    FROM source_files
    WHERE snapshot_id = $snapshotId AND kind = 'in_network' AND status IN ('loaded', 'partial')
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
GROUP BY r.billing_code, r.code_type, r.code
ORDER BY Rates DESC, r.code
