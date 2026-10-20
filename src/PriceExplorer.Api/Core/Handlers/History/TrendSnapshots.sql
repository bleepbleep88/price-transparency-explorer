-- Every snapshot with saved rate data (the rule ResolveSnapshot.sql uses), oldest first: the points a trend can have.
SELECT s.snapshot_id AS SnapshotId, s.index_date AS IndexDate
FROM snapshots s
WHERE EXISTS (
    SELECT 1 FROM source_files f
    WHERE f.snapshot_id = s.snapshot_id AND f.kind = 'in_network' AND f.status IN ('loaded', 'partial'))
ORDER BY s.index_date NULLS FIRST, s.snapshot_id
