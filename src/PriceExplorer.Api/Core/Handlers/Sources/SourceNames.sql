-- Each rate file whose provider names are unreliable (name_checks): the share of shared tax IDs it names as the other
-- files do (its median), the same share between the other files (the median of theirs), and the files the names shown
-- instead come from, most names first, and how many tax IDs have no name to show ("Unknown practice"). One row per
-- file and source file; a file with no source file has one row, with NULL source columns.
WITH typical AS (
    SELECT median(median_agreement) AS share
    FROM name_checks
    WHERE NOT unreliable AND median_agreement IS NOT NULL
),
unknown AS (
    SELECT file_id, count(DISTINCT provider_key) AS tax_ids
    FROM practice_names
    WHERE source = 'unknown'
    GROUP BY file_id
),
sources AS (
    SELECT p.file_id, f.product_label, s.index_date, count(*) AS names
    FROM practice_names p
    JOIN source_files f ON f.file_id = p.source_file_id
    JOIN snapshots s ON s.snapshot_id = f.snapshot_id
    WHERE p.source = 'plan'
    GROUP BY p.file_id, f.product_label, s.index_date
)
SELECT
    n.file_id          AS FileId,
    n.median_agreement AS Agreement,
    t.share            AS TypicalAgreement,
    coalesce(u.tax_ids, 0) AS UnknownTaxIds,
    src.product_label  AS SourceLabel,
    src.index_date     AS SourceDate,
    src.names          AS Names
FROM name_checks n
CROSS JOIN typical t
LEFT JOIN unknown u ON u.file_id = n.file_id
LEFT JOIN sources src ON src.file_id = n.file_id
WHERE n.unreliable
ORDER BY n.file_id, src.names DESC NULLS LAST, src.index_date DESC
