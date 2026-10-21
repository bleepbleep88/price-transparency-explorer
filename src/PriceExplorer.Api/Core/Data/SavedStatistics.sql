-- Whether each snapshot's saved statistics (group_stats, provider_values) still match what search shows. They do while
-- they were built from the snapshot's saved rate files as they are now (each file's id, load time, product key and
-- label: everything SearchRates.sql reads from source_files) and with its provider names as shown now (practice_names,
-- which the directory and newly saved files change), by this build of the app, and no rate in them has passed its
-- expiration date (search stops showing a rate the next day; current_date is fixed for a transaction).
-- Lists every snapshot search can serve (one with a saved rate file, the rule LatestSnapshot.sql and
-- ResolveSnapshot.sql use), newest first; $snapshotId narrows it to one. The statistics build rebuilds the snapshots
-- that aren't current, and readers report it.
WITH saved_files AS (
    SELECT snapshot_id,
           string_agg(concat(file_id, '@', loaded_at, '@', product_key, '@', product_label), ',' ORDER BY file_id) AS rate_files
    FROM source_files
    WHERE kind = 'in_network' AND status IN ('loaded', 'partial')
    GROUP BY snapshot_id
),
shown_names AS (
    SELECT f.snapshot_id,
           md5(string_agg(concat_ws(chr(31), p.file_id, p.provider_key, p.plan_name, p.practice_name), chr(30)
                          ORDER BY p.file_id, p.provider_key, p.plan_name)) AS fingerprint
    FROM practice_names p
    JOIN source_files f ON f.file_id = p.file_id
    GROUP BY f.snapshot_id
),
saved AS (
    SELECT sf.snapshot_id, sf.rate_files || coalesce(' names:' || n.fingerprint, '') AS rate_files
    FROM saved_files sf
    LEFT JOIN shown_names n ON n.snapshot_id = sf.snapshot_id
)
SELECT
    s.snapshot_id    AS SnapshotId,
    s.index_date     AS IndexDate,
    saved.rate_files AS RateFiles,
    b.built_at       AS BuiltAt,
    b.expires_on     AS ExpiresOn,
    coalesce(b.rate_files = saved.rate_files AND b.code_version = $codeVersion
             AND (b.expires_on IS NULL OR b.expires_on >= current_date), false) AS Current
FROM snapshots s
JOIN saved ON saved.snapshot_id = s.snapshot_id
LEFT JOIN stats_builds b ON b.snapshot_id = s.snapshot_id
WHERE $snapshotId::INTEGER IS NULL OR s.snapshot_id = $snapshotId::INTEGER
ORDER BY s.index_date DESC NULLS LAST, s.snapshot_id DESC
