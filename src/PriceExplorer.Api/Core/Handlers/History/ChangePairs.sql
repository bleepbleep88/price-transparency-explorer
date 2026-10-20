-- What the changes feed compares (GetChangesHandler): every comparison group (group_key) and provider (provider_key: the
-- EIN, or the masked key under this install's secret) in either snapshot's saved provider values, joined on both. Never on
-- provider_group_id: the files renumber it every month. Only $products (comma-separated keys: those both snapshots have
-- complete); $codes (comma-separated stored forms) and $codeType narrow to one code. A value is the provider's rate in the
-- group, as its search row shows it. Changes smaller than $minChangePct (percent, up or down) drop out.
-- A new pair is listed_elsewhere when the same provider also has a dropped pair with the same product, code and fee type
-- (and a dropped pair when it has such a new one): the rate moved to other modifiers or another setting (Centene reshuffles
-- telehealth modifiers such as GQ and GT from month to month). A provider that simply has another group in the other file,
-- one it is still in, doesn't count. $listedElsewhere keeps only those (true) or leaves them out (false).
-- ChangeCounts.sql and ChangeRows.sql each end this query with their own SELECT from shown.
WITH older AS (
    SELECT *
    FROM provider_values
    WHERE snapshot_id = $from
      AND list_contains(string_split($products, ','), product_key)
      AND ($codes::VARCHAR IS NULL OR list_contains(string_split($codes::VARCHAR, ','), code))
      AND ($codeType::VARCHAR IS NULL OR code_type = $codeType::VARCHAR)
),
newer AS (
    SELECT *
    FROM provider_values
    WHERE snapshot_id = $to
      AND list_contains(string_split($products, ','), product_key)
      AND ($codes::VARCHAR IS NULL OR list_contains(string_split($codes::VARCHAR, ','), code))
      AND ($codeType::VARCHAR IS NULL OR code_type = $codeType::VARCHAR)
),
pairs AS MATERIALIZED (
    SELECT
        coalesce(n.group_key, o.group_key)             AS group_key,
        coalesce(n.provider_key, o.provider_key)       AS provider_key,
        CASE WHEN o.provider_key IS NULL THEN 'new'
             WHEN n.provider_key IS NULL THEN 'dropped'
             WHEN n.value <> o.value THEN 'changed'
             ELSE 'unchanged' END                      AS kind,
        o.value                                        AS from_value,
        n.value                                        AS to_value,
        CASE WHEN n.value <> o.value THEN n.value - o.value END AS change,
        CASE WHEN n.value <> o.value AND o.value > 0
             THEN round(CAST(n.value - o.value AS DOUBLE) * 100 / CAST(o.value AS DOUBLE), 1) END AS change_pct,
        o.first_rate_id                                AS from_rate_id,
        n.first_rate_id                                AS to_rate_id,
        o.rate_count                                   AS from_rates,
        n.rate_count                                   AS to_rates,
        -- What the newer file says about the provider, unless it was dropped.
        coalesce(n.product_key, o.product_key)         AS product_key,
        coalesce(n.code_type, o.code_type)             AS code_type,
        coalesce(n.code, o.code)                       AS code,
        coalesce(n.fee_type, o.fee_type)               AS fee_type,
        coalesce(n.setting_key, o.setting_key)         AS setting_key,
        coalesce(n.modifiers, o.modifiers)             AS modifiers,
        coalesce(n.negotiated_type, o.negotiated_type) AS negotiated_type,
        CASE WHEN n.provider_key IS NULL THEN o.ein ELSE n.ein END AS ein,
        coalesce(n.ein_display, o.ein_display)         AS ein_display,
        coalesce(n.ein_unverified, o.ein_unverified)   AS ein_unverified,
        coalesce(n.business_name, o.business_name)     AS business_name
    FROM older o
    FULL OUTER JOIN newer n ON n.group_key = o.group_key AND n.provider_key = o.provider_key
),
flagged AS (
    SELECT p.*,
           p.kind IN ('new', 'dropped') AND EXISTS (
               SELECT 1 FROM pairs q
               WHERE q.kind = CASE WHEN p.kind = 'new' THEN 'dropped' ELSE 'new' END
                 AND q.provider_key = p.provider_key AND q.product_key = p.product_key
                 AND q.code_type = p.code_type AND q.code = p.code AND q.fee_type = p.fee_type) AS listed_elsewhere
    FROM pairs p
),
shown AS (
    SELECT *
    FROM flagged
    -- A change from $0 has no percentage, so a minimum leaves it out.
    WHERE (kind <> 'changed' OR $minChangePct::DOUBLE IS NULL OR abs(change_pct) >= $minChangePct::DOUBLE)
      AND ($listedElsewhere::BOOLEAN IS NULL OR listed_elsewhere = $listedElsewhere::BOOLEAN)
)
