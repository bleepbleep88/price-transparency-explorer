-- Every reject reason recorded while reading each file, most frequent first.
SELECT file_id AS FileId, reason AS Reason, count AS Count, example AS Example
FROM ingestion_rejects
ORDER BY file_id, count DESC, reason
