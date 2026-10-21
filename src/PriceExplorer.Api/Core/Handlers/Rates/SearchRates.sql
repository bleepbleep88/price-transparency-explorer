-- One row per unique rate x place of service x provider (tax ID) for the searched code(s).
-- Each provider filter gets its own flag, so an empty result can say which filter is the reason:
--   NpiMatch / EinMatch: one of the provider's groups on this rate lists the NPI / has the EIN.
--   IdMatch: both hold in the SAME group ("this NPI bills under this EIN").
--   NameMatch: a group's business name, as published or as shown, contains the name filter.
--   SpecialtyMatch / CountyMatch: a group lists an NPI with that specialty / practicing in that New York county or
--   borough (provider directory, see directory_matches). With both filters, one NPI must meet both: a gastroenterologist
--   who practices in Manhattan, not a gastroenterologist in Queens next to someone else in Manhattan.
-- FilterMatch: one group meets every filter; MatchedBusinessNames are those groups' names (a tax ID with several
-- facilities shows the one that matched). WithoutName / WithoutSpecialty / WithoutCounty / WithoutDirectory: one group
-- meets every filter but that one (the specialty and location together for WithoutDirectory), so a hint knows whether
-- clearing it finds rates. EinLevel: the rate comes from a contract with the whole tax ID (npi [0]).
-- NpiCount (summed over the provider's groups for the rate) weights the provider's rates when it has several.
-- BusinessNames are the names shown: practice_name() swaps a network's shared name, or a name from an unreliable file,
-- for the practice's own (PracticeNames.sql). ListedAs pairs each swapped name as published with the name shown
-- (fields split by chr(31), pairs by chr(30)), so the row can say "Listed by the plan as …".
-- Grouping and statistics happen in RateSearchAssembler; the column order must match RateFactRow.
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
-- Groups listing an NPI that meets the specialty and/or location filter (none when neither is set).
directory_groups AS (
    SELECT gn.file_id, gn.provider_ref_id, gn.group_idx,
           bool_or(dm.specialty_match)                     AS has_specialty,
           bool_or(dm.county_match)                        AS has_county,
           bool_or(dm.specialty_match AND dm.county_match) AS has_both
    FROM directory_matches($specialty::VARCHAR, $county::VARCHAR) dm
    JOIN group_npis gn ON gn.npi = dm.npi
    JOIN target_files tf ON tf.file_id = gn.file_id
    GROUP BY gn.file_id, gn.provider_ref_id, gn.group_idx
),
linked AS (
    SELECT
        m.rate_id,
        pg.provider_key,
        pg.ein, pg.ein_unverified, pg.ein_level, pg.business_name, pg.npi_count,
        practice_name(pg.file_id, pg.provider_key, pg.business_name) AS shown_name,
        ($npi::BIGINT IS NULL OR EXISTS (
            SELECT 1 FROM group_npis gn
            WHERE gn.file_id = pg.file_id AND gn.provider_ref_id = pg.provider_ref_id
              AND gn.group_idx = pg.group_idx AND gn.npi = $npi::BIGINT)) AS npi_match,
        ($ein::VARCHAR IS NULL OR pg.provider_key = $ein::VARCHAR OR pg.ein = $ein::VARCHAR) AS ein_match,
        ($provider::VARCHAR IS NULL
            OR provider_name_matches(pg.file_id, pg.provider_key, pg.business_name, $provider::VARCHAR)) AS name_match,
        ($specialty::VARCHAR IS NULL OR coalesce(dg.has_specialty, false)) AS specialty_match,
        ($county::VARCHAR IS NULL OR coalesce(dg.has_county, false)) AS county_match,
        CASE WHEN $specialty::VARCHAR IS NOT NULL AND $county::VARCHAR IS NOT NULL THEN coalesce(dg.has_both, false)
             ELSE ($specialty::VARCHAR IS NULL OR coalesce(dg.has_specialty, false))
                  AND ($county::VARCHAR IS NULL OR coalesce(dg.has_county, false))
        END AS directory_match
    FROM matched m
    JOIN rate_provider_refs rr ON rr.rate_id = m.rate_id
    JOIN provider_groups pg ON pg.file_id = rr.file_id AND pg.provider_ref_id = rr.provider_ref_id
    LEFT JOIN directory_groups dg
        ON dg.file_id = pg.file_id AND dg.provider_ref_id = pg.provider_ref_id AND dg.group_idx = pg.group_idx
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
    bool_or(l.ein_level)                     AS EinLevel,
    string_agg(DISTINCT l.shown_name, ' | ' ORDER BY l.shown_name) AS BusinessNames,
    CAST(sum(l.npi_count) AS INTEGER)        AS NpiCount,
    bool_or(l.npi_match AND l.ein_match AND l.name_match AND l.directory_match) AS FilterMatch,
    bool_or(l.npi_match AND l.ein_match)     AS IdMatch,
    bool_or(l.name_match)                    AS NameMatch,
    bool_or(l.npi_match)                     AS NpiMatch,
    bool_or(l.ein_match)                     AS EinMatch,
    string_agg(DISTINCT l.shown_name, ' | ' ORDER BY l.shown_name)
        FILTER (WHERE l.npi_match AND l.ein_match AND l.name_match AND l.directory_match) AS MatchedBusinessNames,
    bool_or(l.specialty_match)               AS SpecialtyMatch,
    bool_or(l.county_match)                  AS CountyMatch,
    bool_or(l.npi_match AND l.ein_match AND l.directory_match) AS WithoutName,
    bool_or(l.npi_match AND l.ein_match AND l.name_match AND l.county_match) AS WithoutSpecialty,
    bool_or(l.npi_match AND l.ein_match AND l.name_match AND l.specialty_match) AS WithoutCounty,
    bool_or(l.npi_match AND l.ein_match AND l.name_match) AS WithoutDirectory,
    string_agg(DISTINCT l.business_name || chr(31) || l.shown_name, chr(30) ORDER BY l.business_name || chr(31) || l.shown_name)
        FILTER (WHERE l.shown_name <> l.business_name) AS ListedAs
FROM matched m
JOIN rate_places rp ON rp.rate_id = m.rate_id
JOIN linked l ON l.rate_id = m.rate_id
GROUP BY m.rate_id, m.product_key, m.product_label, m.code_type, m.code, m.code_source, m.billing_code_type,
         m.billing_code, m.billing_class, m.negotiated_type, m.modifiers, m.rate, m.occurrences, rp.pos, l.provider_key
ORDER BY m.rate_id, rp.pos, l.provider_key
