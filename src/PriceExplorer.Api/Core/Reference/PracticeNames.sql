-- The name to show for each provider group whose published business name doesn't name its own practice. Queries read
-- it through practice_name() (PracticeNameMacro.sql); provider_groups keeps the plan's names as published, and the source
-- drawer and NPI dialog say "Listed by the plan as …".
--
-- Two cases:
--   Shared: an IPA or network that contracts for its members puts its own name on their tax IDs ("Chinese American IPA
--   Assoc/CAIPA" is on 253 of Ambetter's Sep 28 tax IDs), so a list of places reads one name six times. A name is
--   shared when $sharedTaxIds or more tax IDs carry it in a snapshot's reliable files (spaces and case ignored). The tax
--   ID that uses it most (its home, often the network itself) keeps it; the others get their own names.
--   Unreliable file: a rate file whose names disagree with the other files for the same tax IDs (name_checks,
--   NameChecks.sql). The Sep 28 Essential Plan file names EIN 13-4000192 "Samaritan Hospital of Troy" and "Jamaica
--   Hospital" where every other file calls it "ENT & Allergy Associates, LLP". Every name in it is replaced when a better
--   one is found.
--
-- A tax ID's own name is the first found of:
--   1. a name the reliable files give it that is its own (not shared, or shared and it's the home): its own snapshot
--      first, then the nearest other one, the same plan first, then the most groups;
--   2. the NPPES organization name most of its NPIs carry (in that snapshot's files);
--   3. its clinician's NPPES name and credential, when it lists one NPI and that NPI is a person.
-- Without one, the plan's name stays: a snapshot with one plan, or the oldest month, falls back the same way when no
-- other file names the tax ID. Deactivated NPIs don't count. source_file_id is the file a name of the first kind came
-- from (the sources page cites it). Rebuilt at startup and after a snapshot's rate files load, after name_checks.
CREATE OR REPLACE TABLE practice_names AS
WITH files AS (
    SELECT f.file_id, f.snapshot_id, f.product_key, s.index_date
    FROM source_files f
    JOIN snapshots s ON s.snapshot_id = f.snapshot_id
    WHERE f.kind = 'in_network' AND f.status IN ('loaded', 'partial')
),
named AS (
    SELECT fl.snapshot_id, fl.index_date, fl.product_key, pg.file_id, pg.provider_ref_id, pg.group_idx, pg.provider_key,
           pg.business_name, pg.npi_count, upper(regexp_replace(trim(pg.business_name), '\s+', ' ', 'g')) AS name_key
    FROM provider_groups pg
    JOIN files fl ON fl.file_id = pg.file_id
    WHERE trim(coalesce(pg.business_name, '')) <> ''
),
reliable AS (
    SELECT * FROM named WHERE file_id NOT IN (SELECT file_id FROM name_checks WHERE unreliable)
),
uses AS (
    SELECT snapshot_id, name_key, provider_key, count(*) AS groups, sum(npi_count) AS npis
    FROM reliable
    GROUP BY snapshot_id, name_key, provider_key
),
homes AS (
    SELECT snapshot_id, name_key, count(*) >= $sharedTaxIds AS shared,
           first(provider_key ORDER BY groups DESC, npis DESC, provider_key) AS home
    FROM uses
    GROUP BY snapshot_id, name_key
),
-- Names each tax ID owns, per reliable file, as that file spells them.
candidates AS (
    SELECT r.file_id, r.snapshot_id, r.index_date, r.product_key, r.provider_key, r.business_name AS name,
           count(*) AS groups, sum(r.npi_count) AS npis
    FROM reliable r
    JOIN homes h ON h.snapshot_id = r.snapshot_id AND h.name_key = r.name_key
    WHERE NOT h.shared OR h.home = r.provider_key
    GROUP BY r.file_id, r.snapshot_id, r.index_date, r.product_key, r.provider_key, r.business_name
),
-- The plan's names to replace, exactly as published, per file.
listed AS (
    SELECT DISTINCT n.file_id, n.snapshot_id, n.index_date, n.product_key, n.provider_key, n.business_name AS plan_name
    FROM reliable n
    JOIN homes h ON h.snapshot_id = n.snapshot_id AND h.name_key = n.name_key
    WHERE h.shared AND h.home <> n.provider_key
    UNION
    SELECT DISTINCT file_id, snapshot_id, index_date, product_key, provider_key, business_name
    FROM named
    WHERE file_id IN (SELECT file_id FROM name_checks WHERE unreliable)
),
targets AS (
    SELECT DISTINCT file_id, snapshot_id, index_date, product_key, provider_key FROM listed
),
own AS (
    SELECT t.file_id, t.provider_key,
           first(c.name ORDER BY c.snapshot_id <> t.snapshot_id, abs(coalesce(date_diff('day', c.index_date, t.index_date), 100000)),
                 c.product_key <> t.product_key, c.groups DESC, c.npis DESC, c.name, c.file_id) AS name,
           first(c.file_id ORDER BY c.snapshot_id <> t.snapshot_id, abs(coalesce(date_diff('day', c.index_date, t.index_date), 100000)),
                 c.product_key <> t.product_key, c.groups DESC, c.npis DESC, c.name, c.file_id) AS source_file_id
    FROM targets t
    JOIN candidates c ON c.provider_key = t.provider_key
    GROUP BY t.file_id, t.provider_key
),
tax_ids AS (
    SELECT DISTINCT snapshot_id, provider_key FROM targets
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
           CASE WHEN own.name IS NOT NULL THEN 'plan' WHEN organization.name IS NOT NULL THEN 'organization' ELSE 'clinician' END AS source,
           own.source_file_id
    FROM listed l
    LEFT JOIN own ON own.file_id = l.file_id AND own.provider_key = l.provider_key
    LEFT JOIN organization ON organization.snapshot_id = l.snapshot_id AND organization.provider_key = l.provider_key
    LEFT JOIN clinician ON clinician.snapshot_id = l.snapshot_id AND clinician.provider_key = l.provider_key
)
SELECT file_id, provider_key, plan_name, practice_name, source, source_file_id
FROM resolved
WHERE practice_name IS NOT NULL AND practice_name <> plan_name;
