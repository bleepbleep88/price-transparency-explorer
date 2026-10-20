-- Follows ChangePairs.sql: how many pairs of each kind, under every filter but kind. Column order matches ChangeCounts.
SELECT
    CAST(count(*) FILTER (WHERE kind = 'changed') AS INTEGER)                           AS Changed,
    CAST(count(*) FILTER (WHERE kind = 'changed' AND to_value > from_value) AS INTEGER) AS Increased,
    CAST(count(*) FILTER (WHERE kind = 'changed' AND to_value < from_value) AS INTEGER) AS Decreased,
    CAST(count(*) FILTER (WHERE kind = 'new') AS INTEGER)                               AS New,
    CAST(count(*) FILTER (WHERE kind = 'new' AND listed_elsewhere) AS INTEGER)          AS NewListedElsewhere,
    CAST(count(*) FILTER (WHERE kind = 'dropped') AS INTEGER)                           AS Dropped,
    CAST(count(*) FILTER (WHERE kind = 'dropped' AND listed_elsewhere) AS INTEGER)      AS DroppedListedElsewhere,
    CAST(count(*) FILTER (WHERE kind = 'unchanged') AS INTEGER)                         AS Unchanged
FROM shown
