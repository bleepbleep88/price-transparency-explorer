-- The snapshot shown by default: the newest finished one (ready or partial). Before any snapshot has finished
-- (the very first load), the newest one that already has a rate file saved, so data shows as soon as it exists.
-- This matches "data available" in the loading status: both turn on with the first saved file.
SELECT s.snapshot_id
FROM snapshots s
WHERE EXISTS (
    SELECT 1 FROM source_files f
    WHERE f.snapshot_id = s.snapshot_id AND f.kind = 'in_network' AND f.status IN ('loaded', 'partial'))
ORDER BY s.status IN ('ready', 'partial') DESC, s.index_date DESC NULLS LAST, s.snapshot_id DESC
LIMIT 1
