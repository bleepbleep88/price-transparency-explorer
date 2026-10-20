-- Follows ChangePairs.sql: one page of changed, new and dropped pairs ($kind: one of them, increased or decreased for
-- changed ones by direction, or NULL for all), with the
-- group's product label and unit from the snapshot that has the provider. Every sort ends on group and provider, so pages
-- never overlap. Column order matches ChangeRow.
SELECT
    s.kind                                   AS Kind,
    s.group_key                              AS GroupKey,
    s.product_key                            AS ProductKey,
    coalesce(g.product_label, s.product_key) AS ProductLabel,
    s.code_type                              AS CodeType,
    s.code                                   AS Code,
    s.fee_type                               AS FeeType,
    s.setting_key                            AS SettingKey,
    s.modifiers                              AS Modifiers,
    s.negotiated_type                        AS NegotiatedType,
    coalesce(g.unit, '')                     AS Unit,
    s.provider_key                           AS ProviderKey,
    s.ein                                    AS Ein,
    s.ein_display                            AS EinDisplay,
    s.ein_unverified                         AS EinMasked,
    s.business_name                          AS BusinessName,
    s.from_value                             AS FromValue,
    s.to_value                               AS ToValue,
    s.change                                 AS Change,
    s.change_pct                             AS ChangePct,
    s.from_rate_id                           AS FromRateId,
    s.to_rate_id                             AS ToRateId,
    s.from_rates                             AS FromRates,
    s.to_rates                               AS ToRates,
    s.listed_elsewhere                       AS ListedElsewhere
FROM shown s
LEFT JOIN group_stats g
    ON g.snapshot_id = CASE WHEN s.kind = 'dropped' THEN $from ELSE $to END AND g.group_key = s.group_key
WHERE s.kind <> 'unchanged'
  AND ($kind::VARCHAR IS NULL
       OR s.kind = $kind::VARCHAR
       OR ($kind::VARCHAR = 'increased' AND s.kind = 'changed' AND s.to_value > s.from_value)
       OR ($kind::VARCHAR = 'decreased' AND s.kind = 'changed' AND s.to_value < s.from_value))
ORDER BY
    -- largest: the biggest moves first, then new and dropped rates, highest first.
    CASE WHEN $sort = 'largest' THEN abs(s.change_pct) END DESC NULLS LAST,
    CASE WHEN $sort = 'largest' THEN coalesce(s.to_value, s.from_value) END DESC,
    CASE WHEN $sort = '-changePct' THEN s.change_pct END DESC NULLS LAST,
    CASE WHEN $sort = 'changePct' THEN s.change_pct END ASC NULLS LAST,
    CASE WHEN $sort = 'code' THEN s.code END ASC,
    CASE WHEN $sort = '-code' THEN s.code END DESC,
    CASE WHEN $sort = 'provider' THEN lower(s.business_name) END ASC,
    CASE WHEN $sort = '-provider' THEN lower(s.business_name) END DESC,
    s.group_key,
    s.provider_key
LIMIT $limit OFFSET $offset
