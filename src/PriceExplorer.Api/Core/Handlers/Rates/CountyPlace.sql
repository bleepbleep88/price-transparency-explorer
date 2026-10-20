-- The place the location filter matched, the way hints and headings name it: a borough ("Manhattan"), a county
-- ("Delaware County"), or "New York City" when it matches every borough ("NYC"). NULL when it matches no one.
SELECT CASE
    WHEN count(DISTINCT pd.county_fips) = 1 THEN coalesce(nullif(any_value(pd.borough), ''), any_value(pd.county))
    WHEN count(DISTINCT pd.county_fips) > 1 AND bool_and(coalesce(pd.borough, '') <> '') THEN 'New York City'
END AS Place
FROM directory_matches(NULL, $county::VARCHAR) dm
JOIN provider_directory pd ON pd.npi = dm.npi
WHERE dm.county_match
