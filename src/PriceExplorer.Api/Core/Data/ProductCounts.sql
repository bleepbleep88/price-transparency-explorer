-- What completeness compares (Core/Domain/SnapshotCompleteness.cs), per snapshot and product: unique rates and EINs as
-- counted when each file was saved, and distinct searchable codes among its rates. Same saved files search reads.
-- Column order matches ProductCounts.
WITH files AS (
    SELECT file_id, snapshot_id, product_key, status, rates_unique, distinct_eins
    FROM source_files
    WHERE kind = 'in_network' AND status IN ('loaded', 'partial')
),
codes AS (
    SELECT files.snapshot_id, files.product_key, count(DISTINCT r.code) AS codes
    FROM rates r
    JOIN files ON files.file_id = r.file_id
    GROUP BY files.snapshot_id, files.product_key
)
SELECT
    files.snapshot_id                                    AS SnapshotId,
    s.index_date                                         AS IndexDate,
    files.product_key                                    AS ProductKey,
    bool_and(files.status = 'loaded')                    AS ReadFully,
    CAST(coalesce(sum(files.rates_unique), 0) AS BIGINT)  AS Rates,
    CAST(coalesce(sum(files.distinct_eins), 0) AS BIGINT) AS Eins,
    coalesce(any_value(codes.codes), 0)                  AS Codes
FROM files
JOIN snapshots s ON s.snapshot_id = files.snapshot_id
LEFT JOIN codes ON codes.snapshot_id = files.snapshot_id AND codes.product_key = files.product_key
GROUP BY files.snapshot_id, s.index_date, files.product_key
ORDER BY files.snapshot_id, files.product_key
