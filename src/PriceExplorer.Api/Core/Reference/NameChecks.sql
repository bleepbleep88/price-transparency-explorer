-- Whether each rate file's provider names can be trusted: how often it names a tax ID the way each other file does.
-- Two files are compared over the tax IDs both list, when they share at least $comparableTaxIds; a tax ID agrees when
-- the files give it a name in common (spaces and case ignored). A file is unreliable when it agrees with fewer than
-- $reliableAgreement of them against every one of at least $filesToJudgeBy other files: then it's the odd one out, not
-- them. Two files that disagree only with each other are both kept, since nothing says which is wrong. The limits are
-- named constants in PracticeNames.cs, which says why. median_agreement is the file's middle share against the files
-- that aren't unreliable, for the sources page: 9% for the Sep 28 Essential Plan file, 98% for a typical one. Rebuilt with
-- practice_names, which reads it.
CREATE OR REPLACE TABLE name_checks AS
WITH files AS (
    SELECT file_id
    FROM source_files
    WHERE kind = 'in_network' AND status IN ('loaded', 'partial')
),
key_names AS (
    SELECT pg.file_id, pg.provider_key, list(DISTINCT upper(regexp_replace(trim(pg.business_name), '\s+', ' ', 'g'))) AS names
    FROM provider_groups pg
    JOIN files f ON f.file_id = pg.file_id
    WHERE trim(coalesce(pg.business_name, '')) <> ''
    GROUP BY pg.file_id, pg.provider_key
),
agreement AS (
    SELECT a.file_id, b.file_id AS other_id,
           avg(CASE WHEN len(list_intersect(a.names, b.names)) > 0 THEN 1.0 ELSE 0.0 END) AS share
    FROM key_names a
    JOIN key_names b ON b.provider_key = a.provider_key AND b.file_id <> a.file_id
    GROUP BY a.file_id, b.file_id
    HAVING count(*) >= $comparableTaxIds
),
judged AS (
    SELECT file_id, count(*) AS compared_files, max(share) AS best_agreement,
           count(*) >= $filesToJudgeBy AND max(share) < $reliableAgreement AS unreliable
    FROM agreement
    GROUP BY file_id
),
typical AS (
    SELECT a.file_id, median(a.share) AS median_agreement
    FROM agreement a
    WHERE a.other_id NOT IN (SELECT file_id FROM judged WHERE unreliable)
    GROUP BY a.file_id
)
SELECT
    f.file_id,
    coalesce(j.compared_files, 0)   AS compared_files,
    j.best_agreement,
    t.median_agreement,
    coalesce(j.unreliable, false)   AS unreliable
FROM files f
LEFT JOIN judged j ON j.file_id = f.file_id
LEFT JOIN typical t ON t.file_id = f.file_id;
