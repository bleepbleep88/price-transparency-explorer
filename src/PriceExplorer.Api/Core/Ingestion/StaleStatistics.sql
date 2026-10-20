-- Settled snapshots (ready or partial) whose saved statistics are missing or out of date: built from other rate files
-- (one was saved again since, so its loaded_at changed) or by another build of the app. Newest first, like the default
-- snapshot. RateFiles is read before the build reads any rates, so a file saved in between only causes another rebuild.
WITH saved AS (
    SELECT snapshot_id, string_agg(concat(file_id, '@', loaded_at), ',' ORDER BY file_id) AS rate_files
    FROM source_files
    WHERE kind = 'in_network' AND status IN ('loaded', 'partial')
    GROUP BY snapshot_id
)
SELECT s.snapshot_id AS SnapshotId, s.index_date AS IndexDate, saved.rate_files AS RateFiles
FROM snapshots s
JOIN saved ON saved.snapshot_id = s.snapshot_id
LEFT JOIN stats_builds b ON b.snapshot_id = s.snapshot_id
WHERE s.status IN ('ready', 'partial')
  AND (b.snapshot_id IS NULL OR b.rate_files <> saved.rate_files OR b.code_version <> $codeVersion)
ORDER BY s.index_date DESC NULLS LAST, s.snapshot_id DESC
