-- Every code with rates in the snapshot's loaded in-network files and its count of unique rates, counted the way the
-- code typeahead counts them (rates that haven't expired). Codes are classified in C# (CodeReference), which knows CMS's
-- RBCS categories and the code-range fallback.
SELECT r.code_type AS CodeType, r.code AS Code, count(*) AS Rates
FROM rates r
JOIN source_files f ON f.file_id = r.file_id
WHERE f.snapshot_id = $snapshotId AND f.kind = 'in_network' AND f.status IN ('loaded', 'partial')
  AND (r.expiration_date IS NULL OR r.expiration_date >= current_date)
GROUP BY r.code_type, r.code
