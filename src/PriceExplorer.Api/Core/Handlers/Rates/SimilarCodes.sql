-- Codes in this snapshot that start like the one searched for (suggestions when nothing matched).
WITH target_files AS (
    SELECT file_id
    FROM source_files
    WHERE snapshot_id = $snapshotId AND kind = 'in_network' AND status IN ('loaded', 'partial')
)
SELECT
    r.code_type              AS CodeType,
    r.code                   AS Code,
    count(*)                 AS Rates,
    any_value(cd.name)       AS Name
FROM rates r
JOIN target_files tf ON tf.file_id = r.file_id
LEFT JOIN code_descriptions cd ON cd.file_id = r.file_id AND cd.code_type = r.code_type AND cd.code = r.code
WHERE starts_with(r.code, $prefix)
  AND (r.expiration_date IS NULL OR r.expiration_date >= current_date)
GROUP BY r.code_type, r.code
ORDER BY Rates DESC, r.code
LIMIT 5
