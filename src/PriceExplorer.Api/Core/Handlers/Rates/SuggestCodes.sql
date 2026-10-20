-- Typeahead over codes that actually have rates in the snapshot: by code (leading zeros optional) or by name, the plan's
-- own or else CMS's HCPCS Level II name. $inCategory, when set, is the comma-separated "TYPE:CODE" list of one category's
-- codes (picked in C#, which knows CMS's RBCS classes).
WITH target_files AS (
    SELECT file_id
    FROM source_files
    WHERE snapshot_id = $snapshotId AND kind = 'in_network' AND status IN ('loaded', 'partial')
),
codes AS (
    SELECT
        r.code_type,
        r.code,
        count(*) AS rates,
        bool_or(r.code_source = 'billing_code') AS direct,
        string_agg(DISTINCT CASE WHEN r.code_source = 'additional_information' THEN r.billing_code END, ',') AS revenue_codes
    FROM rates r
    JOIN target_files tf ON tf.file_id = r.file_id
    WHERE (r.expiration_date IS NULL OR r.expiration_date >= current_date)
      AND ($inCategory::VARCHAR IS NULL OR list_contains(string_split($inCategory::VARCHAR, ','), r.code_type || ':' || r.code))
    GROUP BY r.code_type, r.code
),
names AS (
    SELECT cd.code_type, cd.code, any_value(cd.name) AS name, any_value(cd.description) AS description
    FROM code_descriptions cd
    JOIN target_files tf ON tf.file_id = cd.file_id
    GROUP BY cd.code_type, cd.code
)
SELECT
    c.code_type     AS CodeType,
    c.code          AS Code,
    n.name          AS Name,
    n.description   AS Description,
    h.description   AS CmsName,
    c.rates         AS Rates,
    c.direct        AS Direct,
    c.revenue_codes AS RevenueCodes
FROM codes c
LEFT JOIN names n ON n.code_type = c.code_type AND n.code = c.code
LEFT JOIN hcpcs_names h ON c.code_type = 'HCPCS' AND h.code = c.code
WHERE starts_with(c.code, $code)
   OR ($digits <> '' AND starts_with(ltrim(c.code, '0'), $digits))
   OR contains(lower(coalesce(n.name, '')), $text)
   OR contains(lower(coalesce(n.description, '')), $text)
   OR (n.code IS NULL AND contains(lower(coalesce(h.description, '')), $text))
ORDER BY
    CASE WHEN c.code = $code OR ltrim(c.code, '0') = $digits THEN 0
         WHEN starts_with(c.code, $code) OR ($digits <> '' AND starts_with(ltrim(c.code, '0'), $digits)) THEN 1
         ELSE 2 END,
    c.rates DESC,
    c.code
LIMIT $limit
