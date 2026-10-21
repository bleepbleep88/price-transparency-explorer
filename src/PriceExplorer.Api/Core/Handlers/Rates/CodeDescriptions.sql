-- Names/descriptions published in the plan's own HCPCS Level II items (CMS's, public domain). Never a CPT or CDT code's:
-- we have no license for the AMA's or ADA's text (Core/Domain/PublishedText.cs), so they're filtered here too, in case a
-- database saved before that rule still holds some. The requested snapshot's wording wins; another snapshot's is used
-- only when this one has none.
SELECT
    cd.code_type                                                                      AS CodeType,
    cd.code                                                                           AS Code,
    arg_max(cd.name, CASE WHEN f.snapshot_id = $snapshotId THEN 1 ELSE 0 END)         AS Name,
    arg_max(cd.description, CASE WHEN f.snapshot_id = $snapshotId THEN 1 ELSE 0 END)  AS Description,
    max(CASE WHEN f.snapshot_id = $snapshotId THEN 1 ELSE 0 END)                      AS FromSnapshot
FROM code_descriptions cd
JOIN source_files f ON f.file_id = cd.file_id
WHERE cd.code IN $codes
  AND regexp_matches(cd.code, '^[A-CE-V][0-9]{4}$')
GROUP BY cd.code_type, cd.code
ORDER BY FromSnapshot DESC, cd.code_type, cd.code
