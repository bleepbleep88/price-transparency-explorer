-- Names/descriptions published in the plan's own CPT/HCPCS items. Prefers the requested snapshot, then any other.
SELECT
    cd.code_type          AS CodeType,
    cd.code               AS Code,
    any_value(cd.name)    AS Name,
    any_value(cd.description) AS Description,
    max(CASE WHEN f.snapshot_id = $snapshotId THEN 1 ELSE 0 END) AS FromSnapshot
FROM code_descriptions cd
JOIN source_files f ON f.file_id = cd.file_id
WHERE cd.code IN $codes
GROUP BY cd.code_type, cd.code
ORDER BY FromSnapshot DESC
