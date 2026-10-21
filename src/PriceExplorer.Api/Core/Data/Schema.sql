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
    kind                  VARCHAR NOT NULL, -- in_network | allowed_amount (out-of-network allowed amounts)
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
    prices_raw            BIGINT,           -- published prices (allowed_amount files: published payments)
    rates_unique          INTEGER,          -- unique rates (allowed_amount files: unique payments)
    duplicates_removed    BIGINT,
    provider_references   INTEGER,
    provider_groups       INTEGER,
    distinct_npis         INTEGER,
    distinct_eins         INTEGER,
    invalid_npis          INTEGER,
    ein_level_groups      INTEGER,
    unverified_ein_groups INTEGER,          -- allowed_amount files: tax ID entries (allowed_amounts[]) with such an EIN
    dangling_references   INTEGER,
    rejected_prices       BIGINT,           -- allowed_amount files: rejected payments
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
    example VARCHAR          -- the first one seen, as published (the API masks tax ID values)
);

-- Out-of-network allowed amounts (allowed_amount files): what the plan allowed when it paid providers outside its
-- network. Plans list a provider and service only with 20 or more claims in a 90-day window, and each distinct amount
-- once, with no claim count. One row per out_of_network[] item: a billing code and the name it was published under.
CREATE TABLE IF NOT EXISTS oon_items (
    file_id           INTEGER NOT NULL,
    item_idx          INTEGER NOT NULL,  -- position in out_of_network[]
    billing_code_type VARCHAR NOT NULL,
    billing_code      VARCHAR NOT NULL,  -- standard form (RC 0450, CPT 00812)
    billing_code_raw  VARCHAR NOT NULL,  -- as published (450, 812)
    name              VARCHAR,
    description       VARCHAR,
    payments          INTEGER NOT NULL   -- payments published under this item, before duplicates collapse
);

-- Unique payments: one allowed amount for one code, tax ID, place(s) of service and claim form, with its providers.
-- Revenue-code items repeat the same payments under unrelated names, so identical ones collapse (occurrences).
CREATE TABLE IF NOT EXISTS oon_payments (
    payment_id        BIGINT PRIMARY KEY, -- (file_id << 32) | sequence, like rate_id
    file_id           INTEGER NOT NULL,
    billing_code_type VARCHAR NOT NULL,
    billing_code      VARCHAR NOT NULL,   -- standard form
    billing_code_raw  VARCHAR NOT NULL,   -- as published
    tin_type          VARCHAR NOT NULL,   -- ein | npi
    tin_value         VARCHAR NOT NULL,   -- as published (an EIN written as a number lacks its leading zero)
    ein               VARCHAR,            -- 9 digits, leading zero restored; NULL when not an EIN
    ein_unverified    BOOLEAN NOT NULL,   -- IRS never issues this prefix
    provider_key      VARCHAR NOT NULL,   -- same rule as provider_groups.provider_key (ProviderKeys.For)
    service_codes     VARCHAR NOT NULL,   -- place-of-service codes, sorted, comma-separated; '' = not stated
    billing_class     VARCHAR NOT NULL,   -- institutional | professional
    modifiers         VARCHAR NOT NULL,   -- sorted, comma-separated; '' = none
    allowed_amount    DECIMAL(18, 4) NOT NULL,
    occurrences       INTEGER NOT NULL,   -- published copies collapsed into this one
    item_label_count  INTEGER NOT NULL,
    item_labels       VARCHAR,            -- up to 3 item names, ' | ' separated
    item_index        INTEGER NOT NULL,   -- JSON path of the first copy:
    allowed_index     INTEGER NOT NULL,   -- out_of_network[item_index].allowed_amounts[allowed_index]
    payment_index     INTEGER NOT NULL    --   .payments[payment_index]
);

-- The providers on each payment. A provider entry with several NPIs has one row per NPI, all with its billed charge, so
-- billed charges count once per (payment_id, provider_idx); an entry with no NPIs has one row with npi NULL.
CREATE TABLE IF NOT EXISTS oon_providers (
    payment_id    BIGINT NOT NULL,
    provider_idx  INTEGER NOT NULL,       -- position in payments[].providers[]
    billed_charge DECIMAL(18, 4),         -- NULL when not a positive number
    npi           BIGINT,
    npi_valid     BOOLEAN
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
    unit               VARCHAR NOT NULL,  -- what one rate pays for, as the search card says (per procedure, per facility charge, …)
    per_unit           BOOLEAN NOT NULL,  -- rates are prices per unit (drugs, supplies)
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
    snapshot_id          INTEGER NOT NULL,
    group_key            VARCHAR NOT NULL,
    product_key          VARCHAR NOT NULL,
    code_type            VARCHAR NOT NULL,
    code                 VARCHAR NOT NULL,
    fee_type             VARCHAR NOT NULL,
    setting_key          VARCHAR NOT NULL,
    modifiers            VARCHAR NOT NULL,
    negotiated_type      VARCHAR NOT NULL,
    provider_key         VARCHAR NOT NULL,         -- provider_groups.provider_key
    ein                  VARCHAR,                  -- 9 digits; NULL when masked (never-issued prefix) or not an EIN, as search returns it
    ein_display          VARCHAR NOT NULL,         -- 11-3471596, ••-•••4567 (masked), or the tax ID's type and value
    ein_unverified       BOOLEAN NOT NULL,
    ein_level            BOOLEAN NOT NULL,         -- at least one of its rates comes from a whole-tax-ID contract (npi [0])
    business_name        VARCHAR NOT NULL,         -- the name on the provider group(s) behind first_rate_id
    value                DECIMAL(18, 4) NOT NULL,  -- NPI-weighted (lower) median of its rates in the group: always a real rate
    low                  DECIMAL(18, 4) NOT NULL,
    high                 DECIMAL(18, 4) NOT NULL,
    rate_count           INTEGER NOT NULL,
    npi_weight           BIGINT NOT NULL,          -- the weight behind value: each rate's NPI count (at least 1), summed
    first_rate_id        BIGINT NOT NULL,          -- the rate carrying value that lists the most NPIs (the row's source)
    whole_tax_id_rate_id BIGINT,                   -- a whole-tax-ID rate behind ein_level, when first_rate_id isn't one
    rate_set             VARCHAR NOT NULL          -- its distinct rates in the group, ascending, '0.0000' each, comma-separated
);

-- The practice's own name for provider groups whose published name is a network's shared name or comes from an
-- unreliable file (Core/Reference/PracticeNames.sql rebuilds it; practice_name() reads it). Startup also creates it for
-- databases made before it existed (PracticeNameMacro.sql), so adding it needed no schema version bump.
CREATE TABLE IF NOT EXISTS practice_names (
    file_id       INTEGER NOT NULL,
    provider_key  VARCHAR NOT NULL,
    plan_name     VARCHAR NOT NULL,   -- provider_groups.business_name, as published
    practice_name VARCHAR NOT NULL,
    source        VARCHAR NOT NULL    -- plan | organization | clinician: where the name came from
);

-- One row per snapshot with saved statistics: what they were built from. They stay current (SavedStatistics.sql) until
-- a rate file is saved again (its loaded_at changes), a republished index renames or regroups a file's product, the
-- app's code changes, or a rate they include passes its expiration date (search stops showing it the next day). The
-- next load rebuilds what isn't current; readers are told meanwhile. A restart with none of these keeps them.
CREATE TABLE IF NOT EXISTS stats_builds (
    snapshot_id    INTEGER NOT NULL,
    rate_files     VARCHAR NOT NULL,  -- each saved rate file as file_id@loaded_at@product_key@product_label, then a fingerprint of its practice names, if any
    code_version   VARCHAR NOT NULL,  -- the app build (module version id) whose search code built them
    expires_on     DATE,              -- the earliest expiration date among the rates they include; NULL = none ends
    built_at       TIMESTAMP NOT NULL,
    build_ms       INTEGER NOT NULL,
    code_count     INTEGER NOT NULL,
    group_count    INTEGER NOT NULL,
    value_count    INTEGER NOT NULL
);
