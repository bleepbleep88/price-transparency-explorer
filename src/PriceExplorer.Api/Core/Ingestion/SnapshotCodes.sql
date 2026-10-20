-- Every code (stored form) with saved rates in the snapshot, and how many rates it has: the statistics build searches
-- them a batch at a time. Same files as SearchRates.sql reads.
SELECT r.code AS Code, count(*) AS Rates
FROM rates r
JOIN source_files f ON f.file_id = r.file_id
WHERE f.snapshot_id = $snapshotId AND f.kind = 'in_network' AND f.status IN ('loaded', 'partial')
GROUP BY r.code
ORDER BY r.code
