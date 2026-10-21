-- The name to show for each provider group whose published business name doesn't name its own practice, and
-- practice_name(), which every query that shows a provider's name calls. provider_groups keeps the plan's names as
-- published (the source drawer shows them, and the shown name says "Listed by the plan as …" there).
--
-- Two cases:
--   Shared: an IPA or network that contracts for its members puts its own name on their tax IDs ("Chinese American IPA
--   Assoc/CAIPA" is on 253 of Ambetter's Sep 28 tax IDs), so a list of places reads one name five times. A name is
--   shared when 5 or more tax IDs carry it in a snapshot's reliable files (spaces and case ignored). On Sep 28, 5,714
--   of 6,714 names belong to one tax ID and 731 to 2-4 (one practice's few entities, such as a PC and its surgery
--   center), while every IPA but three has 5 or more. The tax ID that uses the name most (its home, often the
--   network itself) keeps it; the others get their own names.
--   Unreliable file: a rate file whose names disagree with every other file's for the same tax IDs. The Sep 28 Essential
--   Plan file names EIN 13-4000192 "Samaritan Hospital of Troy" and "Jamaica Hospital" where both plans call it "ENT &
--   Allergy Associates, LLP" in other months; it agrees with the other files on 6% of the tax IDs they share, against
--   89-94% between any two other files. A file is unreliable when it agrees with fewer than half of the tax IDs it shares
--   with each of at least two other files (100+ shared each), so two files that disagree with only each other are both
--   kept. Every name in it is replaced when a better one is found.
--
-- A tax ID's own name is the first found of:
--   1. a name the reliable files give it that is its own (not shared, or shared and it's the home): from its own
--      snapshot, else the nearest other one; then the most groups;
--   2. the NPPES organization name most of its NPIs carry (in that snapshot's files);
--   3. its clinician's NPPES name and credential, when it lists one NPI and that NPI is a person.
-- Without one, the plan's name stays. Deactivated NPIs don't count. Rebuilt at startup and after rate files load.
CREATE OR REPLACE TABLE practice_names AS
WITH files AS (
    SELECT f.file_id, f.snapshot_id, s.index_date
    FROM source_files f
    JOIN snapshots s ON s.snapshot_id = f.snapshot_id
    WHERE f.kind = 'in_network' AND f.status IN ('loaded', 'partial')
),
named AS (
    SELECT fl.snapshot_id, fl.index_date, pg.file_id, pg.provider_ref_id, pg.group_idx, pg.provider_key, pg.business_name,
           pg.npi_count, upper(regexp_replace(trim(pg.business_name), '\s+', ' ', 'g')) AS name_key
    FROM provider_groups pg
    JOIN files fl ON fl.file_id = pg.file_id
    WHERE trim(coalesce(pg.business_name, '')) <> ''
),
key_names AS (
    SELECT file_id, provider_key, list(DISTINCT name_key) AS names
    FROM named
    GROUP BY file_id, provider_key
),
-- For each pair of files: the share of the tax IDs both list that they name the same way (any name in common).
agreement AS (
    SELECT a.file_id, b.file_id AS other_id,
           avg(CASE WHEN len(list_intersect(a.names, b.names)) > 0 THEN 1.0 ELSE 0.0 END) AS share
    FROM key_names a
    JOIN key_names b ON b.provider_key = a.provider_key AND b.file_id <> a.file_id
    GROUP BY a.file_id, b.file_id
    HAVING count(*) >= 100
),
unreliable AS (
    SELECT file_id
    FROM agreement
    GROUP BY file_id
    HAVING count(*) >= 2 AND max(share) < 0.5
),
reliable AS (
    SELECT * FROM named WHERE file_id NOT IN (SELECT file_id FROM unreliable)
),
uses AS (
    SELECT snapshot_id, name_key, provider_key, count(*) AS groups, sum(npi_count) AS npis
    FROM reliable
    GROUP BY snapshot_id, name_key, provider_key
),
homes AS (
    SELECT snapshot_id, name_key, count(*) >= 5 AS shared,
           first(provider_key ORDER BY groups DESC, npis DESC, provider_key) AS home
    FROM uses
    GROUP BY snapshot_id, name_key
),
-- Each tax ID's own names in each snapshot, spelled as its groups most often spell them.
owned AS (
    SELECT u.snapshot_id, u.provider_key, u.groups, u.npis,
           (SELECT first(r.business_name ORDER BY r.spelled DESC, r.business_name)
            FROM (SELECT business_name, count(*) AS spelled
                  FROM reliable r
                  WHERE r.snapshot_id = u.snapshot_id AND r.provider_key = u.provider_key AND r.name_key = u.name_key
                  GROUP BY business_name) r) AS name
    FROM uses u
    JOIN homes h ON h.snapshot_id = u.snapshot_id AND h.name_key = u.name_key
    WHERE NOT h.shared OR h.home = u.provider_key
),
-- The plan's names to replace, exactly as published, per file.
listed AS (
    SELECT DISTINCT n.file_id, n.snapshot_id, n.index_date, n.provider_key, n.business_name AS plan_name
    FROM reliable n
    JOIN homes h ON h.snapshot_id = n.snapshot_id AND h.name_key = n.name_key
    WHERE h.shared AND h.home <> n.provider_key
    UNION
    SELECT DISTINCT file_id, snapshot_id, index_date, provider_key, business_name
    FROM named
    WHERE file_id IN (SELECT file_id FROM unreliable)
),
tax_ids AS (
    SELECT DISTINCT snapshot_id, index_date, provider_key FROM listed
),
own AS (
    SELECT t.snapshot_id, t.provider_key,
           first(o.name ORDER BY o.snapshot_id <> t.snapshot_id,
                 abs(coalesce(date_diff('day', s.index_date, t.index_date), 100000)), o.groups DESC, o.npis DESC, o.name) AS name
    FROM tax_ids t
    JOIN owned o ON o.provider_key = t.provider_key
    JOIN snapshots s ON s.snapshot_id = o.snapshot_id
    GROUP BY t.snapshot_id, t.provider_key
),
npis AS (
    SELECT DISTINCT n.snapshot_id, n.provider_key, gn.npi
    FROM named n
    JOIN tax_ids t ON t.snapshot_id = n.snapshot_id AND t.provider_key = n.provider_key
    JOIN group_npis gn ON gn.file_id = n.file_id AND gn.provider_ref_id = n.provider_ref_id AND gn.group_idx = n.group_idx
),
registered AS (
    SELECT n.snapshot_id, n.provider_key, n.npi, d.entity_type, trim(d.name) AS name, nullif(trim(d.credential), '') AS credential
    FROM npis n
    JOIN provider_directory d ON d.npi = n.npi
    WHERE NOT coalesce(d.deactivated, false) AND trim(coalesce(d.name, '')) <> ''
),
organization AS (
    SELECT snapshot_id, provider_key, first(name ORDER BY carried DESC, name) AS name
    FROM (
        SELECT snapshot_id, provider_key, name, count(*) AS carried
        FROM registered
        WHERE entity_type = 'organization'
        GROUP BY snapshot_id, provider_key, name)
    GROUP BY snapshot_id, provider_key
),
clinician AS (
    SELECT n.snapshot_id, n.provider_key, any_value(r.name || coalesce(', ' || r.credential, '')) AS name
    FROM npis n
    LEFT JOIN registered r ON r.snapshot_id = n.snapshot_id AND r.provider_key = n.provider_key AND r.npi = n.npi
    GROUP BY n.snapshot_id, n.provider_key
    HAVING count(DISTINCT n.npi) = 1 AND coalesce(bool_and(r.entity_type = 'individual'), false)
),
resolved AS (
    SELECT l.file_id, l.provider_key, l.plan_name,
           coalesce(own.name, organization.name, clinician.name) AS practice_name,
           CASE WHEN own.name IS NOT NULL THEN 'plan' WHEN organization.name IS NOT NULL THEN 'organization' ELSE 'clinician' END AS source
    FROM listed l
    LEFT JOIN own ON own.snapshot_id = l.snapshot_id AND own.provider_key = l.provider_key
    LEFT JOIN organization ON organization.snapshot_id = l.snapshot_id AND organization.provider_key = l.provider_key
    LEFT JOIN clinician ON clinician.snapshot_id = l.snapshot_id AND clinician.provider_key = l.provider_key
)
SELECT file_id, provider_key, plan_name, practice_name, source
FROM resolved
WHERE practice_name IS NOT NULL AND practice_name <> plan_name;
