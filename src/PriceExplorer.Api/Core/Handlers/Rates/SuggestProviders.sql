-- Typeahead over provider names, as shown (practice_name) or as published in a file whose names can be trusted
-- (provider_name_matches), and exact EINs in the snapshot, busiest first. It lists the shown names. A provider found only
-- by a name as published says which (ListedAs), so the page can say "listed by the plan as …". Names are listed in a
-- fixed order (the first is the label) and ties break on the provider key, so the same text always gives the same list.
WITH target_files AS (
    SELECT file_id
    FROM source_files
    WHERE snapshot_id = $snapshotId AND kind = 'in_network' AND status IN ('loaded', 'partial')
),
matches AS (
    SELECT
        pg.provider_key,
        pg.ein, pg.ein_unverified, practice_name(pg.file_id, pg.provider_key, pg.business_name) AS business_name,
        pg.business_name AS listed_name,
        contains(lower(coalesce(practice_name(pg.file_id, pg.provider_key, pg.business_name), '')), $text)
            OR ($digits <> '' AND pg.ein = $digits) AS shown_match,
        pg.file_id, pg.provider_ref_id
    FROM provider_groups pg
    JOIN target_files tf ON tf.file_id = pg.file_id
    WHERE provider_name_matches(pg.file_id, pg.provider_key, pg.business_name, $text)
       OR ($digits <> '' AND pg.ein = $digits)
)
SELECT
    m.provider_key                           AS ProviderKey,
    any_value(m.ein)                         AS Ein,
    bool_or(m.ein_unverified)                AS EinUnverified,
    string_agg(DISTINCT m.business_name, ' | ' ORDER BY m.business_name) AS BusinessNames,
    count(DISTINCT rr.rate_id)               AS Rates,
    CASE WHEN bool_or(m.shown_match) THEN NULL ELSE min(m.listed_name) END AS ListedAs
FROM matches m
LEFT JOIN rate_provider_refs rr ON rr.file_id = m.file_id AND rr.provider_ref_id = m.provider_ref_id
GROUP BY m.provider_key
ORDER BY Rates DESC, BusinessNames, ProviderKey
LIMIT $limit
