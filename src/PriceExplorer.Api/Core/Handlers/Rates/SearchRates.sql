-- One row per unique rate x place of service x provider (tax ID) for the searched code(s).
-- FilterMatch: this provider matches the NPI / EIN / name filters. NPI and EIN must match within the SAME provider
-- group, i.e. "this NPI bills under this EIN". Grouping and statistics happen in RateSearchAssembler.
WITH target_files AS (
    SELECT file_id, product_key, product_label
    FROM source_files
    WHERE snapshot_id = $snapshotId AND kind = 'in_network' AND status IN ('loaded', 'partial')
),
matched AS (
    SELECT r.rate_id, r.file_id, tf.product_key, tf.product_label, r.code_type, r.code, r.code_source,
           r.billing_code_type, r.billing_code, r.billing_class, r.negotiated_type, r.modifiers, r.rate, r.occurrences
    FROM rates r
    JOIN target_files tf ON tf.file_id = r.file_id
    WHERE r.code IN $codes
      AND (r.expiration_date IS NULL OR r.expiration_date >= current_date)
),
linked AS (
    SELECT
        m.rate_id,
        CASE
            WHEN pg.ein IS NULL THEN pg.tin_type || ':' || coalesce(pg.tin_value, '')
            WHEN pg.ein_unverified THEN 'u' || substr(sha256(pg.ein), 1, 12)
            ELSE pg.ein
        END AS provider_key,
        pg.ein, pg.ein_unverified, pg.ein_level, pg.business_name, pg.npi_count,
        (
            ($npi::BIGINT IS NULL OR EXISTS (
                SELECT 1 FROM group_npis gn
                WHERE gn.file_id = pg.file_id AND gn.provider_ref_id = pg.provider_ref_id
                  AND gn.group_idx = pg.group_idx AND gn.npi = $npi::BIGINT))
            AND ($ein::VARCHAR IS NULL OR pg.ein = $ein::VARCHAR
                 OR (pg.ein_unverified AND 'u' || substr(sha256(pg.ein), 1, 12) = $ein::VARCHAR))
            AND ($provider::VARCHAR IS NULL OR contains(lower(coalesce(pg.business_name, '')), $provider::VARCHAR))
        ) AS filter_match
    FROM matched m
    JOIN rate_provider_refs rr ON rr.rate_id = m.rate_id
    JOIN provider_groups pg ON pg.file_id = rr.file_id AND pg.provider_ref_id = rr.provider_ref_id
)
SELECT
    m.rate_id                                AS RateId,
    m.product_key                            AS ProductKey,
    m.product_label                          AS ProductLabel,
    m.code_type                              AS CodeType,
    m.code                                   AS Code,
    m.code_source                            AS CodeSource,
    m.billing_code_type                      AS BillingCodeType,
    m.billing_code                           AS BillingCode,
    m.billing_class                          AS BillingClass,
    m.negotiated_type                        AS NegotiatedType,
    m.modifiers                              AS Modifiers,
    m.rate                                   AS Rate,
    m.occurrences                            AS Occurrences,
    rp.pos                                   AS Pos,
    l.provider_key                           AS ProviderKey,
    any_value(l.ein)                         AS Ein,
    bool_or(l.ein_unverified)                AS EinUnverified,
    bool_and(l.ein_level)                    AS EinLevel,
    string_agg(DISTINCT l.business_name, ' | ') AS BusinessNames,
    CAST(sum(l.npi_count) AS INTEGER)        AS NpiCount,
    bool_or(l.filter_match)                  AS FilterMatch
FROM matched m
JOIN rate_places rp ON rp.rate_id = m.rate_id
JOIN linked l ON l.rate_id = m.rate_id
GROUP BY m.rate_id, m.product_key, m.product_label, m.code_type, m.code, m.code_source, m.billing_code_type,
         m.billing_code, m.billing_class, m.negotiated_type, m.modifiers, m.rate, m.occurrences, rp.pos, l.provider_key
