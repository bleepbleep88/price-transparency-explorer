-- practice_name(file, provider, name): the name to show for a provider group, its practice's own name when
-- PracticeNames.sql found one, else the plan's name as published. Every query that shows a provider's name calls it, so
-- search rows, places, the scorecard, compare and changes agree. Created at startup before the first rebuild, so
-- queries never meet a missing macro, with the tables (as in Schema.sql) for databases made before they existed.
CREATE TABLE IF NOT EXISTS practice_names (
    file_id        INTEGER NOT NULL,
    provider_key   VARCHAR NOT NULL,
    plan_name      VARCHAR NOT NULL,  -- provider_groups.business_name, as published
    practice_name  VARCHAR NOT NULL,
    source         VARCHAR NOT NULL,  -- plan | organization | clinician | unknown: where the name came from
    source_file_id INTEGER            -- with source plan: the rate file the name comes from
);

-- Whether each rate file's provider names can be trusted (Core/Reference/NameChecks.sql).
CREATE TABLE IF NOT EXISTS name_checks (
    file_id        INTEGER NOT NULL,
    compared_files INTEGER NOT NULL,  -- other files sharing enough tax IDs to compare with
    best_agreement DOUBLE,            -- the most of their shared tax IDs any of them names the same way; NULL = none compared
    median_agreement DOUBLE,          -- the middle share against the files that aren't unreliable
    unreliable     BOOLEAN NOT NULL
);

CREATE OR REPLACE MACRO practice_name(in_file, of_provider, listed_name) AS coalesce(
    (SELECT p.practice_name FROM practice_names p
     WHERE p.file_id = in_file AND p.provider_key = of_provider AND p.plan_name = listed_name),
    listed_name);

-- A provider name filter: the typed text (lower case) appears in the group's name as published or as shown, so a
-- practice is found by either.
CREATE OR REPLACE MACRO provider_name_matches(in_file, of_provider, listed_name, text) AS
    contains(lower(coalesce(listed_name, '')), text)
    OR contains(lower(coalesce(practice_name(in_file, of_provider, listed_name), '')), text);
