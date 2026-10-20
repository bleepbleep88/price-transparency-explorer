-- Typeahead over provider business names (and exact EINs) in the snapshot, busiest first. Names are listed in a fixed
-- order (the first is the label) and ties break on the provider key, so the same text always gives the same list.
WITH target_files AS (
    SELECT file_id
    FROM source_files
    WHERE snapshot_id = $snapshotId AND kind = 'in_network' AND status IN ('loaded', 'partial')
),
matches AS (
    SELECT
        pg.provider_key,
        pg.ein, pg.ein_unverified, pg.business_name, pg.file_id, pg.provider_ref_id
    FROM provider_groups pg
    JOIN target_files tf ON tf.file_id = pg.file_id
    WHERE contains(lower(coalesce(pg.business_name, '')), $text)
       OR ($digits <> '' AND pg.ein = $digits)
)
SELECT
    m.provider_key                           AS ProviderKey,
    any_value(m.ein)                         AS Ein,
    bool_or(m.ein_unverified)                AS EinUnverified,
    string_agg(DISTINCT m.business_name, ' | ' ORDER BY m.business_name) AS BusinessNames,
    count(DISTINCT rr.rate_id)               AS Rates
FROM matches m
LEFT JOIN rate_provider_refs rr ON rr.file_id = m.file_id AND rr.provider_ref_id = m.provider_ref_id
GROUP BY m.provider_key
ORDER BY Rates DESC, BusinessNames, ProviderKey
LIMIT $limit
