-- The newest snapshot older than $snapshotId (by index date) with saved rate data: what a change is measured from by default.
SELECT s.snapshot_id
FROM snapshots s
WHERE s.index_date < (SELECT index_date FROM snapshots WHERE snapshot_id = $snapshotId)
  AND EXISTS (
      SELECT 1 FROM source_files f
      WHERE f.snapshot_id = s.snapshot_id AND f.kind = 'in_network' AND f.status IN ('loaded', 'partial'))
ORDER BY s.index_date DESC, s.snapshot_id DESC
LIMIT 1
