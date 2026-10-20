-- Which NPIs in the provider directory match a specialty and/or a location filter (either may be NULL). Search, hints
-- and row profiles all use it, so they agree on what a match is. Created at startup, after the table it reads.
--   specialty: text in the NUCC specialty name or its grouping ("gastro", "anesthe"), or an exact taxonomy code.
--   county: a New York county or New York City borough, by name ("Manhattan", "Monroe", "Kings County") or FIPS code
--   ("36061"). Only New York counties (FIPS 36...) match: "Monroe" is Monroe County, NY, never Monroe County, PA.
-- NPIs that meet neither filter are left out.
CREATE OR REPLACE MACRO directory_matches(specialty, county) AS TABLE
SELECT *
FROM (
    SELECT
        pd.npi,
        coalesce(contains(lower(pd.specialty), lower(trim(specialty)))
                 OR contains(lower(pd.specialty_group), lower(trim(specialty)))
                 OR lower(pd.taxonomy_code) = lower(trim(specialty)), false) AS specialty_match,
        coalesce(starts_with(pd.county_fips, '36')
                 AND lower(trim(county)) IN (lower(pd.borough), lower(pd.county), lower(replace(pd.county, ' County', '')), pd.county_fips),
                 false) AS county_match
    FROM provider_directory pd
)
WHERE specialty_match OR county_match
