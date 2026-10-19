WITH loaded AS (
    SELECT snapshot_id, index_date
    FROM snapshots
    WHERE status IN ('ready', 'partial')
),
latest AS (
    SELECT snapshot_id
    FROM loaded
    ORDER BY index_date DESC NULLS LAST, snapshot_id DESC
    LIMIT 1
)
SELECT
    l.snapshot_id                                     AS SnapshotId,
    l.index_date                                      AS IndexDate,
    l.snapshot_id = (SELECT snapshot_id FROM latest)  AS IsLatest
FROM loaded l
WHERE ($snapshotId::INTEGER IS NULL AND l.snapshot_id = (SELECT snapshot_id FROM latest))
   OR l.snapshot_id = $snapshotId::INTEGER
