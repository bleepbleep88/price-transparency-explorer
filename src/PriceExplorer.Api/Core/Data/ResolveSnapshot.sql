-- A snapshot with saved rate data (at least one rate file loaded, fully or partly).
SELECT s.snapshot_id AS SnapshotId, s.index_date AS IndexDate
FROM snapshots s
WHERE s.snapshot_id = $snapshotId
  AND EXISTS (
      SELECT 1 FROM source_files f
      WHERE f.snapshot_id = s.snapshot_id AND f.kind = 'in_network' AND f.status IN ('loaded', 'partial'))
