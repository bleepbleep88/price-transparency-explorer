-- How many NPIs in the provider directory match the specialty filter and the location filter, each on its own, and how
-- many it holds at all (0 when it isn't loaded). Hints use it to tell a typo, or a place with no providers in these
-- files, from a specialty or place that has providers but no rate for the code.
SELECT
    (SELECT count(*) FROM provider_directory)  AS Npis,
    count(*) FILTER (WHERE dm.specialty_match) AS SpecialtyNpis,
    count(*) FILTER (WHERE dm.county_match)    AS CountyNpis
FROM directory_matches($specialty::VARCHAR, $county::VARCHAR) dm
