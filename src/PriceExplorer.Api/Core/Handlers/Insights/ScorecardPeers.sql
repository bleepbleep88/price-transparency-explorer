-- Every provider's saved value (the provider's own included) in the groups where a provider has a value and the group
-- has a median (5+ providers): the values search ranks a row against for its percentile.
SELECT o.group_key AS GroupKey, o.value AS Value
FROM provider_values o
JOIN group_stats g ON g.snapshot_id = o.snapshot_id AND g.group_key = o.group_key
WHERE o.snapshot_id = $snapshotId
  AND g.stats_level <> 'list'
  AND o.group_key IN (SELECT v.group_key FROM provider_values v WHERE v.snapshot_id = $snapshotId AND v.provider_key = $provider)
ORDER BY o.group_key, o.value
