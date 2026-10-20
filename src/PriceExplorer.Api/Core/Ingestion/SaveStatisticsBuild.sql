-- Records what a snapshot's statistics were built from (StaleStatistics.sql compares it). expires_on is the earliest
-- expiration date among the rates they include: search stops showing that rate the next day, and so must they. It runs
-- in the build's transaction, so current_date is the date SearchRates.sql filtered on while building.
INSERT INTO stats_builds (snapshot_id, rate_files, code_version, expires_on, built_at, build_ms, code_count, group_count, value_count)
SELECT
    $snapshotId,
    $rateFiles,
    $codeVersion,
    (SELECT min(r.expiration_date)
     FROM rates r
     JOIN source_files f ON f.file_id = r.file_id
     WHERE f.snapshot_id = $snapshotId AND f.kind = 'in_network' AND f.status IN ('loaded', 'partial')
       AND r.expiration_date >= current_date),
    $builtAt,
    $buildMs,
    $codeCount,
    $groupCount,
    $valueCount
