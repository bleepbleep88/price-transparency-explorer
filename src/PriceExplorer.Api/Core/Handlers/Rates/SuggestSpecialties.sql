-- Specialties (NUCC names) of the NPIs in the provider directory that the specialty filter would match for this text,
-- the most NPIs first. Uses the filter's own rule (directory_matches), so every suggestion finds what it says.
SELECT pd.specialty AS Specialty, any_value(pd.specialty_group) AS "Group", count(*) AS Npis
FROM directory_matches($text::VARCHAR, NULL) dm
JOIN provider_directory pd ON pd.npi = dm.npi
WHERE coalesce(pd.specialty, '') <> ''
GROUP BY pd.specialty
ORDER BY Npis DESC, Specialty
LIMIT $limit
