-- New York counties and New York City boroughs where NPIs in the provider directory practice: boroughs first, then
-- counties with the most NPIs. Each label is what the location filter accepts ("Manhattan", "Monroe").
SELECT
    CASE WHEN coalesce(borough, '') <> '' THEN borough ELSE replace(county, ' County', '') END AS Label,
    bool_or(coalesce(borough, '') <> '')                                                    AS IsBorough,
    count(*)                                                                                AS Npis
FROM provider_directory
WHERE starts_with(county_fips, '36')
GROUP BY 1
ORDER BY IsBorough DESC, Npis DESC, Label
