-- Names/descriptions published in the plan's own CPT/HCPCS items. The requested snapshot's wording wins; another
-- snapshot's is used only when this one has none.
SELECT
    cd.code_type                                                                      AS CodeType,
    cd.code                                                                           AS Code,
    arg_max(cd.name, CASE WHEN f.snapshot_id = $snapshotId THEN 1 ELSE 0 END)         AS Name,
    arg_max(cd.description, CASE WHEN f.snapshot_id = $snapshotId THEN 1 ELSE 0 END)  AS Description,
    max(CASE WHEN f.snapshot_id = $snapshotId THEN 1 ELSE 0 END)                      AS FromSnapshot
FROM code_descriptions cd
JOIN source_files f ON f.file_id = cd.file_id
WHERE cd.code IN $codes
GROUP BY cd.code_type, cd.code
ORDER BY FromSnapshot DESC, cd.code_type, cd.code
