-- The provider key for what was asked for: a key as results carry it, or a tax ID's full number (a masked one included,
-- which results show only as ••-•••1234), the way search reads its EIN filter. Only provider groups that carry a
-- current rate in the snapshot's rate files count, as in search.
SELECT pg.provider_key
FROM provider_groups pg
JOIN source_files f ON f.file_id = pg.file_id
WHERE f.snapshot_id = $snapshotId AND f.kind = 'in_network' AND f.status IN ('loaded', 'partial')
  AND (pg.provider_key = $provider OR pg.ein = $provider)
  AND EXISTS (
      SELECT 1
      FROM rate_provider_refs rr
      JOIN rates r ON r.rate_id = rr.rate_id
      WHERE rr.file_id = pg.file_id AND rr.provider_ref_id = pg.provider_ref_id
        AND (r.expiration_date IS NULL OR r.expiration_date >= current_date))
ORDER BY pg.provider_key
LIMIT 1
