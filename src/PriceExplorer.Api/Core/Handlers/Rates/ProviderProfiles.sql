-- For each row on the current page: the distinct NPIs behind it, over ALL of the row's rates, and who they are in the
-- provider directory. The count is the row's NPI count, so the profile, the row and its NPI list always agree.
-- {rows} is replaced with a VALUES list of ($iN, $kN, rate id) triples (row index, provider key, rate id) built by the
-- handler. Per row: one 'totals' line (NPIs; how many the directory has; active individuals; deactivated; active imaging
-- readers and cardiologists, for the imaging flag), then one line per specialty and per place, with their NPI counts. Matches marks the specialties and places that meet the search's
-- specialty and location filters, so they can be listed first. Places read "Manhattan, NY" in New York City and
-- "Rome, NY (Oneida County)" elsewhere, so a county filter visibly matches.
WITH page(row_index, provider_key, rate_id) AS (
    VALUES {rows}
),
row_groups AS (
    SELECT DISTINCT p.row_index, pg.file_id, pg.provider_ref_id, pg.group_idx
    FROM page p
    JOIN rate_provider_refs rr ON rr.rate_id = p.rate_id
    JOIN provider_groups pg ON pg.file_id = rr.file_id AND pg.provider_ref_id = rr.provider_ref_id
    WHERE pg.provider_key = p.provider_key
),
row_npis AS (
    SELECT DISTINCT g.row_index, gn.npi
    FROM row_groups g
    JOIN group_npis gn ON gn.file_id = g.file_id AND gn.provider_ref_id = g.provider_ref_id AND gn.group_idx = g.group_idx
),
described AS (
    SELECT
        n.row_index, n.npi, pd.npi IS NOT NULL AS found, pd.entity_type, pd.taxonomy_code, pd.specialty, pd.specialty_group, pd.city,
        pd.state, pd.county, pd.borough,
        coalesce(pd.deactivated, false)     AS deactivated,
        coalesce(dm.specialty_match, false) AS matches_specialty,
        coalesce(dm.county_match, false)    AS matches_county
    FROM row_npis n
    LEFT JOIN provider_directory pd ON pd.npi = n.npi
    LEFT JOIN directory_matches($specialty::VARCHAR, $county::VARCHAR) dm ON dm.npi = n.npi
)
SELECT
    CAST(r.row_index AS BIGINT) AS RowIndex,
    'totals'                    AS Kind,
    NULL                        AS Label,
    NULL                        AS GroupName,
    count(d.npi)                AS Npis,
    count(d.npi) FILTER (WHERE d.found)                                            AS Found,
    count(d.npi) FILTER (WHERE d.entity_type = 'individual' AND NOT d.deactivated) AS Individuals,
    count(d.npi) FILTER (WHERE d.deactivated)                                      AS Deactivated,
    -- Radiologists of every kind (taxonomy codes 2085*, but not radiation oncology or therapeutic radiology, which treat), nuclear
    -- medicine physicians (207U*) and radiology centers read CT, MRI and PET.
    count(d.npi) FILTER (WHERE NOT d.deactivated AND (
        (d.taxonomy_code LIKE '2085%' AND d.taxonomy_code NOT IN ('2085R0001X', '2085R0203X'))
        OR d.taxonomy_code LIKE '207U%' OR d.taxonomy_code = '261QR0200X'))           AS ImagingReaders,
    -- Cardiologists, who also read cardiac CT, MRI and PET: general, electrophysiology, interventional, heart failure, pediatric.
    count(d.npi) FILTER (WHERE NOT d.deactivated AND d.taxonomy_code IN (
        '207RC0000X', '207RC0001X', '207RI0011X', '207RA0001X', '2080P0202X'))          AS CardiacReaders,
    false                       AS Matches
FROM (SELECT DISTINCT row_index FROM row_groups) r
LEFT JOIN described d ON d.row_index = r.row_index
GROUP BY r.row_index
UNION ALL
SELECT CAST(row_index AS BIGINT), 'specialty', specialty, any_value(specialty_group), count(*), 0, 0, 0, 0, 0, bool_or(matches_specialty)
FROM described
WHERE coalesce(specialty, '') <> ''
GROUP BY row_index, specialty
UNION ALL
SELECT
    CAST(row_index AS BIGINT),
    'place',
    CASE WHEN coalesce(borough, '') <> '' THEN borough || ', NY'
         WHEN coalesce(county, '') <> '' THEN city || ', ' || state || ' (' || county || ')'
         ELSE city || ', ' || state END,
    NULL, count(*), 0, 0, 0, 0, 0, bool_or(matches_county)
FROM described
WHERE coalesce(city, '') <> ''
GROUP BY 1, 3
