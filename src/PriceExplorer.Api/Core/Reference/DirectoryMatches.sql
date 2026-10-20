-- Which NPIs in the provider directory match a specialty and/or a location filter (either may be NULL). Search, hints,
-- row profiles and the specialty typeahead all use it, so they agree on what a match is. Created at startup, after the
-- table it reads. With neither filter it reads nothing.
--   specialty_text is matched against the NUCC specialty name ("Urology Physician"), never its grouping.
--     - A full specialty name, which is what picking a suggestion sends, matches that specialty only.
--     - An exact taxonomy code ("207RG0100X") matches that code.
--     - Other text: every word must start a word of the name, so "urology" never matches "Neurology Physician" and
--       "gi" never matches "Surgical". A few common forms are read the way the registry writes them: titles
--       ("cardiologist" → cardiolog, "psychiatrist" → psychiatr, "surgeon" → surg, "pediatrician" → pediatric),
--       "anesthesia", British spellings ("orthopaedic" and "orthopedic" are one word), ENT, GI and OB/GYN. Cardiology
--       also finds the "Cardiovascular Disease" physicians, NUCC's name for general cardiologists.
--   county_text: a New York county or New York City borough by name ("Manhattan", "Monroe", "Kings County",
--   "The Bronx", "St Lawrence", "Brooklyn, NY"), by FIPS code ("36061"), or "NYC" for all five boroughs. Only New York
--   counties (FIPS 36...) match: "Monroe" is Monroe County, NY, never Monroe County, PA.
-- NPIs that meet neither filter are left out.
CREATE OR REPLACE MACRO directory_matches(specialty_text, county_text) AS TABLE
WITH wanted AS (
    SELECT
        lower(trim(specialty_text)) AS text,
        -- The typed words, in the registry's spelling. ENT and GI become markers the name rule below expands.
        list_filter(string_split_regex(
            regexp_replace(regexp_replace(regexp_replace(regexp_replace(regexp_replace(regexp_replace(regexp_replace(
                regexp_replace(regexp_replace(lower(trim(specialty_text)),
                    'orthopaed', 'orthoped', 'g'), 'paediatr', 'pediatr', 'g'), 'anaesth', 'anesth', 'g'),
                    '(olog|iatr)ists?\b', '\1', 'g'), '\bpediatricians?\b', 'pediatric', 'g'), '\bsurgeons?\b', 'surg', 'g'),
                    '\banesthesia\b', 'anesthe', 'g'), '\bob\W*gyn\b', 'obstetric gynecolog', 'g'),
                    '\b(ent|gi)\b', '\1_alias', 'g'),
            '[^a-z0-9_]+'), word -> word <> '') AS words,
        regexp_replace(regexp_replace(regexp_replace(regexp_replace(lower(trim(county_text)),
            '^the\s+', ''), '(,\s*|\s+)(ny|new york)$', ''), '\s+(county|borough)$', ''), '^(saint|st)\.?\s+', 'st. ') AS place
),
-- The distinct specialty names (about 400), so the word rules run once per name rather than once per NPI.
names AS (
    SELECT DISTINCT specialty AS name
    FROM provider_directory
    WHERE specialty_text IS NOT NULL AND specialty IS NOT NULL
),
picked AS (
    SELECT names.name FROM names, wanted WHERE lower(names.name) = wanted.text
),
matched_names AS (
    SELECT name FROM picked
    UNION ALL
    SELECT names.name
    FROM names, wanted
    WHERE NOT EXISTS (SELECT 1 FROM picked)
      AND len(wanted.words) > 0
      AND list_bool_and(list_transform(wanted.words, word ->
          regexp_matches(replace(replace(lower(names.name), 'orthopaed', 'orthoped'), 'paediatr', 'pediatr'),
              '(^|[^a-z0-9])' || CASE
                  WHEN word = 'ent_alias' THEN 'otolaryngolog'
                  WHEN word = 'gi_alias' THEN 'gastroenterolog'
                  WHEN starts_with(word, 'cardiolog') THEN '(cardiolog|cardiovascular)'
                  ELSE word END)))
)
SELECT npi, specialty_match, county_match
FROM (
    SELECT
        pd.npi,
        coalesce(pd.specialty IN (SELECT name FROM matched_names)
                 OR (NOT EXISTS (SELECT 1 FROM picked) AND lower(pd.taxonomy_code) = w.text), false) AS specialty_match,
        coalesce(starts_with(pd.county_fips, '36') AND (
                w.place IN (lower(pd.borough), lower(replace(pd.county, ' County', '')), pd.county_fips)
                OR (w.place IN ('nyc', 'new york city') AND coalesce(pd.borough, '') <> '')),
            false) AS county_match
    FROM provider_directory pd, wanted w
    WHERE specialty_text IS NOT NULL OR county_text IS NOT NULL
)
WHERE specialty_match OR county_match
