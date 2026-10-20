-- The products each of the two snapshots lists rate files for, with their labels, and whether any of the product's
-- files is saved (loaded, or partly read). A product whose files all failed is listed but not saved.
SELECT
    snapshot_id                                 AS SnapshotId,
    product_key                                 AS ProductKey,
    min(product_label)                          AS ProductLabel,
    bool_or(status IN ('loaded', 'partial'))    AS Saved
FROM source_files
WHERE kind = 'in_network' AND snapshot_id IN ($from, $to)
GROUP BY snapshot_id, product_key
ORDER BY product_key
