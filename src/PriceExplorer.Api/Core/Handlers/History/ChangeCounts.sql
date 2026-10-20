-- Follows ChangePairs.sql: how many included pairs of each kind, under every filter but kind; and how many revenue-code-
-- only pairs the switch would add to the rows, under every filter including kind. Column order matches ChangeCounts.
SELECT
    CAST(count(*) FILTER (WHERE included AND kind = 'changed') AS INTEGER)                           AS Changed,
    CAST(count(*) FILTER (WHERE included AND kind = 'changed' AND to_value > from_value) AS INTEGER) AS Increased,
    CAST(count(*) FILTER (WHERE included AND kind = 'changed' AND to_value < from_value) AS INTEGER) AS Decreased,
    CAST(count(*) FILTER (WHERE included AND kind = 'new') AS INTEGER)                               AS New,
    CAST(count(*) FILTER (WHERE included AND kind = 'new' AND listed_elsewhere) AS INTEGER)          AS NewListedElsewhere,
    CAST(count(*) FILTER (WHERE included AND kind = 'dropped') AS INTEGER)                           AS Dropped,
    CAST(count(*) FILTER (WHERE included AND kind = 'dropped' AND listed_elsewhere) AS INTEGER)      AS DroppedListedElsewhere,
    CAST(count(*) FILTER (WHERE included AND kind = 'unchanged') AS INTEGER)                         AS Unchanged,
    CAST(count(*) FILTER (WHERE code_type = 'RC' AND kind <> 'unchanged' AND of_kind) AS INTEGER)    AS FacilityCharges
FROM shown
