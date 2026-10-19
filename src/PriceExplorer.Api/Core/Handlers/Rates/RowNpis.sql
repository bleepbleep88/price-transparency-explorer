-- Distinct NPIs behind each row on the current page: the provider's NPIs listed under the row's rates.
-- {rows} is replaced with a VALUES list of ($iN, $kN, $rN) triples (row index, provider key, rate id) built by the handler.
WITH page(row_index, provider_key, rate_id) AS (
    VALUES {rows}
),
row_groups AS (
    SELECT DISTINCT p.row_index, pg.file_id, pg.provider_ref_id, pg.group_idx
    FROM page p
    JOIN rate_provider_refs rr ON rr.rate_id = p.rate_id
    JOIN provider_groups pg ON pg.file_id = rr.file_id AND pg.provider_ref_id = rr.provider_ref_id
    WHERE p.provider_key = CASE
        WHEN pg.ein IS NULL THEN pg.tin_type || ':' || coalesce(pg.tin_value, '')
        WHEN pg.ein_unverified THEN 'u' || substr(sha256(pg.ein), 1, 12)
        ELSE pg.ein END
)
SELECT CAST(g.row_index AS BIGINT) AS RowIndex, count(DISTINCT gn.npi) AS Npis
FROM row_groups g
LEFT JOIN group_npis gn ON gn.file_id = g.file_id AND gn.provider_ref_id = g.provider_ref_id AND gn.group_idx = g.group_idx
GROUP BY g.row_index
