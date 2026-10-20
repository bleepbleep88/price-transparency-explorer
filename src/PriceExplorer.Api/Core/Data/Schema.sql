-- Explorer database. Everything here is derived from the payer's files and can be rebuilt from the download cache.

CREATE TABLE IF NOT EXISTS meta (
    key   VARCHAR PRIMARY KEY,
    value VARCHAR NOT NULL
);

-- One row per table-of-contents (index) file.
CREATE TABLE IF NOT EXISTS snapshots (
    snapshot_id           INTEGER PRIMARY KEY,
    index_url             VARCHAR NOT NULL UNIQUE,
    index_date            DATE,          -- date in the index file name
    index_last_updated_on DATE,          -- last_updated_on inside the index
    reporting_entity_name VARCHAR,
    reporting_entity_type VARCHAR,
    schema_version        VARCHAR,
    index_sha256          VARCHAR,
    status                VARCHAR NOT NULL, -- pending | loading | ready | partial | failed
    error                 VARCHAR,
    updated_at            TIMESTAMP
);

-- Plans listed in the index. Every plan in a reporting structure shares that structure's rate files.
CREATE TABLE IF NOT EXISTS plans (
    snapshot_id       INTEGER NOT NULL,
    structure_index   INTEGER NOT NULL,
    plan_name         VARCHAR,
    plan_id_type      VARCHAR,
    plan_id           VARCHAR,
    plan_market_type  VARCHAR,
    issuer_name       VARCHAR,
    plan_sponsor_name VARCHAR
);

-- Which rate files each reporting structure (plan group) points to. Several structures may share one file;
-- it is downloaded and parsed once, and its plans are the plans of every structure that lists it.
CREATE TABLE IF NOT EXISTS structure_files (
    snapshot_id     INTEGER NOT NULL,
    structure_index INTEGER NOT NULL,
    url             VARCHAR NOT NULL
);

-- One row per data file referenced by an index. structure_index is the first structure that lists it.
CREATE TABLE IF NOT EXISTS source_files (
    file_id               INTEGER PRIMARY KEY,
    snapshot_id           INTEGER NOT NULL,
    kind                  VARCHAR NOT NULL, -- in_network
    structure_index       INTEGER NOT NULL,
    product_key           VARCHAR NOT NULL,
    product_label         VARCHAR NOT NULL,
    url                   VARCHAR NOT NULL,
    description           VARCHAR,
    status                VARCHAR NOT NULL, -- pending | loaded | partial | failed
    error                 VARCHAR,
    warning               VARCHAR,
    parser_version        INTEGER,
    sha256                VARCHAR,
    etag                  VARCHAR,
    http_last_modified    TIMESTAMP,
    content_length        BIGINT,
    file_last_updated_on  DATE,
    file_schema_version   VARCHAR,
    reporting_entity_name VARCHAR,
    items                 INTEGER,
    prices_raw            BIGINT,
    rates_unique          INTEGER,
    duplicates_removed    BIGINT,
    provider_references   INTEGER,
    provider_groups       INTEGER,
    distinct_npis         INTEGER,
    distinct_eins         INTEGER,
    invalid_npis          INTEGER,
    ein_level_groups      INTEGER,
    unverified_ein_groups INTEGER,
    dangling_references   INTEGER,
    rejected_prices       BIGINT,
    download_ms           INTEGER,
    parse_ms              INTEGER,
    loaded_at             TIMESTAMP,
    UNIQUE (snapshot_id, kind, url)
);

-- provider_references[].provider_groups[] from each file. provider_ref_id is only unique within its file.
CREATE TABLE IF NOT EXISTS provider_groups (
    file_id         INTEGER NOT NULL,
    provider_ref_id INTEGER NOT NULL,
    group_idx       INTEGER NOT NULL,
    tin_type        VARCHAR,
    tin_value       VARCHAR,
    ein             VARCHAR,          -- 9 digits, no dash
    business_name   VARCHAR,
    npi_count       INTEGER NOT NULL,
    ein_level       BOOLEAN NOT NULL, -- npi: [0]: the contract covers the whole TIN
    ein_unverified  BOOLEAN NOT NULL, -- IRS never issues this prefix
    network_names   VARCHAR,
    provider_key    VARCHAR NOT NULL  -- how results identify the provider: EIN, 'u' + HMAC key when unverified, or type:value (ProviderKeys.For)
);

CREATE TABLE IF NOT EXISTS group_npis (
    file_id         INTEGER NOT NULL,
    provider_ref_id INTEGER NOT NULL,
    group_idx       INTEGER NOT NULL,
    npi             BIGINT NOT NULL,
    npi_valid       BOOLEAN NOT NULL
);

-- Unique negotiated prices after collapsing duplicates.
CREATE TABLE IF NOT EXISTS rates (
    rate_id                 BIGINT PRIMARY KEY,
    file_id                 INTEGER NOT NULL,
    billing_code_type       VARCHAR NOT NULL,
    billing_code            VARCHAR NOT NULL, -- standard form (RC 0490, CPT 00812)
    billing_code_raw        VARCHAR NOT NULL, -- as published (490, 812)
    code_type               VARCHAR NOT NULL, -- searchable code type (CPT/HCPCS for facility rows that name one)
    code                    VARCHAR NOT NULL,
    code_source             VARCHAR NOT NULL, -- billing_code | additional_information
    negotiation_arrangement VARCHAR NOT NULL,
    negotiated_type         VARCHAR NOT NULL,
    billing_class           VARCHAR NOT NULL,
    setting                 VARCHAR NOT NULL,
    modifiers               VARCHAR NOT NULL, -- sorted, comma-separated; '' = none
    rate                    DECIMAL(18, 4) NOT NULL,
    expiration_date         DATE,             -- NULL = no end date (published as 9999-12-31)
    additional_information  VARCHAR,
    occurrences             INTEGER NOT NULL, -- published rows collapsed into this one
    item_label_count        INTEGER NOT NULL,
    item_labels             VARCHAR,          -- up to 3 source item names, ' | ' separated
    item_index              INTEGER NOT NULL, -- JSON path of the first occurrence
    rate_index              INTEGER NOT NULL,
    price_index             INTEGER NOT NULL
);

-- Place-of-service codes per rate (a rate can list several). '' = not specified.
CREATE TABLE IF NOT EXISTS rate_places (
    rate_id BIGINT NOT NULL,
    pos     VARCHAR NOT NULL
);

CREATE TABLE IF NOT EXISTS rate_provider_refs (
    rate_id         BIGINT NOT NULL,
    file_id         INTEGER NOT NULL,
    provider_ref_id INTEGER NOT NULL
);

-- Names/descriptions from each file's own CPT/HCPCS items (never from revenue-code items, whose names are unreliable).
CREATE TABLE IF NOT EXISTS code_descriptions (
    file_id     INTEGER NOT NULL,
    code_type   VARCHAR NOT NULL,
    code        VARCHAR NOT NULL,
    name        VARCHAR,
    description VARCHAR
);

CREATE TABLE IF NOT EXISTS ingestion_rejects (
    file_id INTEGER NOT NULL,
    reason  VARCHAR NOT NULL,
    count   BIGINT NOT NULL,
    example VARCHAR
);

-- Saved statistics: what GET /api/v1/rates shows with no provider filter, for every code of a snapshot, so history and
-- insights can read them instead of searching each code. Built after loading by SnapshotStatisticsBuilder from the
-- search's own code (SearchRates.sql + RateSearchAssembler), all of a snapshot in one transaction. Column order
-- matches the builder's appenders.

-- One row per comparison group: product × code type × code × fee type × setting × modifiers × rate type.
CREATE TABLE IF NOT EXISTS group_stats (
    snapshot_id        INTEGER NOT NULL,
    group_key          VARCHAR NOT NULL,  -- the search's group key: the seven parts below joined with '|'
    product_key        VARCHAR NOT NULL,
    product_label      VARCHAR NOT NULL,
    code_type          VARCHAR NOT NULL,
    code               VARCHAR NOT NULL,
    fee_type           VARCHAR NOT NULL,  -- facility | professional
    setting_key        VARCHAR NOT NULL,  -- care setting (settings.tsv), from the place of service
    modifiers          VARCHAR NOT NULL,  -- sorted, comma-separated; '' = none
    negotiated_type    VARCHAR NOT NULL,
    provider_count     INTEGER NOT NULL,  -- tax IDs, each counted once at its value
    rate_count         INTEGER NOT NULL,
    stats_level        VARCHAR NOT NULL,  -- list (< 5 providers) | basic (5-9) | full (10+)
    min                DECIMAL(18, 4) NOT NULL,
    p10                DECIMAL(18, 4),    -- quantiles: linear interpolation, rounded to cents; p10/p90 full only,
    p25                DECIMAL(18, 4),    -- p25/median/p75 basic and full
    median             DECIMAL(18, 4),
    p75                DECIMAL(18, 4),
    p90                DECIMAL(18, 4),
    max                DECIMAL(18, 4) NOT NULL,
    spread_multiple    DECIMAL(18, 4),    -- max ÷ min, full only
    p90_to_p10         DECIMAL(18, 4),    -- full only
    lower_quartile_gap DECIMAL(18, 4)     -- median − p25, full only
);

-- One row per provider (tax ID) in each comparison group: its one value, which the group's statistics count.
CREATE TABLE IF NOT EXISTS provider_values (
    snapshot_id     INTEGER NOT NULL,
    group_key       VARCHAR NOT NULL,
    product_key     VARCHAR NOT NULL,
    code_type       VARCHAR NOT NULL,
    code            VARCHAR NOT NULL,
    fee_type        VARCHAR NOT NULL,
    setting_key     VARCHAR NOT NULL,
    modifiers       VARCHAR NOT NULL,
    negotiated_type VARCHAR NOT NULL,
    provider_key    VARCHAR NOT NULL,         -- provider_groups.provider_key
    ein             VARCHAR,                  -- 9 digits; NULL when masked (never-issued prefix) or not an EIN, as search returns it
    ein_display     VARCHAR NOT NULL,         -- 11-3471596, ••-•••4567 (masked), or the tax ID's type and value
    ein_unverified  BOOLEAN NOT NULL,
    ein_level       BOOLEAN NOT NULL,         -- at least one of its rates comes from a whole-tax-ID contract (npi [0])
    business_name   VARCHAR NOT NULL,         -- the name on the provider group(s) behind first_rate_id
    value           DECIMAL(18, 4) NOT NULL,  -- NPI-weighted (lower) median of its rates in the group: always a real rate
    low             DECIMAL(18, 4) NOT NULL,
    high            DECIMAL(18, 4) NOT NULL,
    rate_count      INTEGER NOT NULL,
    npi_weight      BIGINT NOT NULL,          -- the weight behind value: each rate's NPI count (at least 1), summed
    first_rate_id   BIGINT NOT NULL           -- the rate carrying value that lists the most NPIs (the row's source)
);

-- One row per snapshot with saved statistics: what they were built from. A snapshot is rebuilt when a rate file is
-- saved again (its loaded_at changes) or the app's code changes; a restart with unchanged files keeps them.
CREATE TABLE IF NOT EXISTS stats_builds (
    snapshot_id    INTEGER NOT NULL,
    rate_files     VARCHAR NOT NULL,  -- each saved rate file as file_id@loaded_at
    code_version   VARCHAR NOT NULL,  -- the app build (module version id) whose search code built them
    built_at       TIMESTAMP NOT NULL,
    build_ms       INTEGER NOT NULL,
    code_count     INTEGER NOT NULL,
    group_count    INTEGER NOT NULL,
    value_count    INTEGER NOT NULL
);
