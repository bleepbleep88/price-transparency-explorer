-- The products with saved rate files in a snapshot, by name.
SELECT DISTINCT product_key AS "Key", product_label AS "Label"
FROM source_files
WHERE snapshot_id = $snapshotId AND kind = 'in_network' AND status IN ('loaded', 'partial')
ORDER BY "Label", "Key"
