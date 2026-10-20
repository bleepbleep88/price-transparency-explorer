-- The products each of the two snapshots has saved rate files for, with their labels.
SELECT snapshot_id AS SnapshotId, product_key AS ProductKey, min(product_label) AS ProductLabel
FROM source_files
WHERE kind = 'in_network' AND status IN ('loaded', 'partial') AND snapshot_id IN ($from, $to)
GROUP BY snapshot_id, product_key
ORDER BY product_key
