-- The snapshot's allowed-amount files, whatever their status: which products could have out-of-network amounts.
SELECT
    file_id              AS FileId,
    product_key          AS ProductKey,
    product_label        AS ProductLabel,
    url                  AS Url,
    status               AS Status,
    file_last_updated_on AS FileLastUpdatedOn
FROM source_files
WHERE snapshot_id = $snapshotId AND kind = 'allowed_amount'
ORDER BY structure_index, file_id
