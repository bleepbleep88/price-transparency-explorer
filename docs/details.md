# Details

The rules, findings and numbers behind the app, for readers who want more than the [README](../README.md). The design and diagrams are in [architecture.md](architecture.md).

## Who it's for

One neutral explorer. Each page leads with one answer (a price, a percent or a count) in plain words. On a search, an "I'm a…" row adds one line for each audience:

| Audience | Question it answers |
|---|---|
| Providers | Where does my rate sit against other providers for the same service and setting, and how has it moved? |
| Health plans | Where are prices spread widest, which rates are outliers, and what changed since the last file? |
| Employers | What does a service typically cost, how much less do lower-priced providers get, and is the typical price moving? |
| Innovators | How clean is this data, and can I trace and build on every number? |
| Life sciences | What does a drug or device cost per unit, by site of care? |

## What the data looks like (measured on the 2026-08-31 files)

| Finding | Numbers | What the app does |
|---|---|---|
| Facility rates hide the procedure code | Hospital and surgery-center rates are filed under revenue codes, with the CPT in `additional_information`. Knee arthroscopy (CPT 29881) exists **only** as revenue code 0490 rows. | The CPT becomes the searchable code. The result says "facility rate under revenue code 0490". |
| Most rows are duplicates | Ambetter: 179,948 published prices → 7,727 unique. Essential Plan: 359,853 → 9,822. About 96% are repeats. | Collapsed at load, with a count of copies kept for each rate. |
| Revenue-code item names are unreliable | The 29881 rows appear under 151 different item names. | Kept only as source labels, never used as the procedure's name. |
| Leading zeros are stripped | Revenue code `490` = 0490. Anesthesia CPT `812` = 00812, priced per anesthesia unit ($7.94). | Codes are restored to standard form; the published value is kept. Search accepts either. |
| Professional fees are mostly fee schedules | For one code, 49 of 51 providers have exactly $167.09. Facility fees for the same procedure vary about 5×. | Groups where most providers share one rate are labeled as a fee schedule, not a negotiation. |
| Some provider IDs are invalid or sensitive | 2 NPIs fail the check digit. 326 (Ambetter) and 102 (Essential Plan) tax IDs use EIN prefixes the IRS never issues (likely Social Security numbers). 5 groups list `npi: [0]`, meaning a contract with the whole tax ID. | Invalid NPIs are flagged. Never-issued EINs are masked (`••-•••1234`) and identified by a key that can't be turned back into the number (an HMAC under a secret made once per install). Whole-tax-ID contracts are labeled. |
| One NPI, many listings | 8,094 of 43,833 Ambetter NPIs appear in more than one provider group. | Results count one value per provider (tax ID) per comparison group. |
| One tax ID, different rates per clinician | Teladoc's 99213 telehealth rate is $45 for most of its 319 NPIs and $90 for others. | The row shows the rate most of its NPIs are paid, with the range; the NPI list shows each NPI's own rate. An NPI search shows that NPI's rate. |
| Telehealth depends on where the patient is | 141 provider-code pairs pay telehealth at home (POS 10) differently from elsewhere (POS 02). | Two separate settings. |
| Group IDs are local to each file | `provider_group_id` is renumbered in every file and every month. | Never used as a key across files. |
| Three dates per file | The index date in the file name (2026-08-31), `last_updated_on` inside each file (2026-09-02), and HTTP Last-Modified (2026-09-04). | All three are kept and shown in the source drawer. |
| Rates that aren't comparable | Clinic (FQHC/RHC) rates are all-inclusive per-visit payments. | Kept in their own setting, never compared with office fees. |
| What's absent | Every price has `expiration_date` 9999-12-31 (no end date). No inpatient DRGs; codes are CPT, HCPCS or revenue codes only. | Expiry shown as "no end date". The scope is outpatient and professional rates. |
| Out-of-network amounts are thin, repeated, and don't say what they cover | Plans list a provider and service only with 20+ claims in a 90-day window: 33 codes (Ambetter) and 115 (Essential Plan). Revenue-code items repeat the same payments under unrelated names: 237 published → 103 unique, 1,033 → 605. Many amounts are far above one service: CPT 97530 (a 15-minute therapy unit, about $32 in network) has a middle listed amount of $1,814.70, and a BLS ambulance code reaches $1,038,971. The files don't say whether an amount covers one visit or several. | Duplicates collapse. A small panel on search results leads with how much providers billed against what the plan allowed (Essential Plan ER facility: about 4.8 times; ambulance: about the same), which holds whatever an amount covers. The amounts themselves show only as a range, never a typical figure, and every figure needs 5 providers: with fewer, each amount is listed and nothing is compared. |
| EINs written as numbers | 6 EINs in the Essential Plan allowed-amount file have 8 digits (60646659 for 06-0646659). | The leading zero is restored before the IRS prefix check. |
| Networks put their name on their members' tax IDs | In Ambetter's 2026-09-28 file, "Chinese American IPA Assoc/CAIPA" is on 253 tax IDs, so a search for 00731 listed it six times for six different practices. In the 2026-08-31 files, 7,071 of 7,094 names are on one tax ID, and the 8 on five or more are networks such as Catholic Medical Partners (123) and Beacon IPA (107). | On a name on 5+ tax IDs, each practice shows its own name: one the files give that tax ID that isn't shared, else its organization's name in the national provider registry (NPPES), else its clinician's when it lists one. A practice with none keeps the network's name, and so does the network's own tax ID (the one using the name most, when it has no other name). The plan's name stays in the source drawer and NPI list as "Listed by the plan as …". |
| The 2026-09-28 Essential Plan file mislabels providers | It names most tax IDs after other providers: ENT & Allergy Associates (EIN 13-4000192) appears as "Samaritan Hospital of Troy", "Jamaica Hospital" and three other names. It names a tax ID the way the other files do for 9% of the tax IDs it shares with them; any two other files agree on 85 to 100%. | A file that agrees with fewer than half the shared tax IDs, against every other file, is treated as mislabelled, and the Sources page says so. Its names are shown only when another file gives the same tax ID the same name (282 of its 4,262 tax IDs). The others take their name from Ambetter's Sep 28 file (2,462), Essential Plan's Aug 31 file (1,260), other months (46) or a lone clinician in NPPES (78). The 134 no other file or NPPES names read "Unknown practice", with the plan's name under "Listed by the plan as …". |

## Comparing fairly

Rates are compared only like for like. A **comparison group** is:

product × code × fee type (facility or professional) × care setting × modifiers × rate type

Each provider (tax ID) counts once per group. If it has several rates in one group, the one that counts is the NPI-weighted median: the rate in the middle of its clinicians. That is always a rate someone is really paid, never an average of two. The row shows the range.

### Plain numbers, simple pages

The people who read these pages aren't statisticians. Every page is built so anyone can read it at a glance:
- Each page leads with one answer, big: a price per bill ("Doctor $168", "Surgery center $511"), "Same price in both plans: 77%", or "Usually 18% above typical".
- Under it come a short list of places and a few plain words.
- Codes, modifiers, tax IDs, NPI counts, file facts and method text sit behind one "Details".

No page says percentile, median, quartile, "×" or index. A page makes only claims the files support, and the API keeps the precise fields for anyone building on it.

Shared pieces keep every page the same:
- `web/src/lib/plainNumbers.ts` (figures) and `web/src/lib/plainNames.ts` (plain names for places, bills, services and providers), each with its own tests.
- The components in `web/src/components/simple/`.

The end-to-end tests load every kind of page on the real data. They fail if a page shows a statistics word, a code or an ID on its main view, or more than 220 words.

| On the page | What it is | Needs |
|---|---|---|
| "Only 3 places: $300 · $300 · $450" | each price listed, nothing compared or ranked | under 5 providers |
| "$578 typical" | the median | 5 providers |
| "From $185 to $205" | lowest to highest | 5–9 providers |
| "Most: $372 – $664" | 10th to 90th percentile | 10 providers |
| "The cheapest quarter of places get at least $124 less per case." | median − 25th percentile (`lowerQuartileGap`) | 10 providers |
| "Lower than most", "About typical", "Higher than most"; prices in green or red | at or below the 25th percentile, within 5% of typical, at or above the 75th; with dollars worked out from the figures as shown, so they add up | 5 providers |
| "about 2.4 times the typical price" | a ratio, said as "times" only from 2×; below that, dollars | |

Plans and employers read the cheapest quarter as room to steer care. Providers read it as negotiating context. It is never called savings. The files have no claim counts, so it is never multiplied into a total unless you enter a number of cases yourself. The footer says once that these are what the plan pays, not what a patient pays.

Filtering by NPI, EIN, name, specialty or location narrows the rows to the matching providers' rates (an NPI search shows that NPI's own rate), but the statistics stay market-wide. So a provider is shown as "Yours: $143.75 · Lower than most · $24.25 less than typical" against everyone else. A provider paid the fee-schedule rate that most of its group gets reads "About typical", not "Lower than most". Care settings come from CMS place-of-service codes. Hospital outpatient (POS 19 and 22) is one setting. Clinics, and telehealth at home versus elsewhere, each have their own.

## Who is behind each rate

The rate files list bare NPIs. `reference/provider_directory.csv.gz` (1.9 MB) gives the 58,688 NPIs in the files a name, specialty and practice place, from the October 2026 NPPES release, the NUCC taxonomy and Census ZIP-to-county data. `tools/PriceExplorer.ReferenceBuilder` rebuilds it (see `reference/README.md`).

- Each provider row shows who its NPIs are: "Gastroenterology Physician · Manhattan, NY · +12 more".
- Filter by **specialty** or by **New York county or NYC borough**. With both, one NPI must have that specialty and practice there. Like an NPI filter, they pick rows; the statistics stay market-wide. A specialty picked from the list finds just that specialty. Typed words match the start of words in specialty names, so "urology" never finds neurologists, and common forms work ("cardiologist", "ENT", "OB/GYN", "orthopaedic"). Locations take county or borough names ("Kings County", "The Bronx") or "NYC".
- **Check this** flags mark listed rates that look unusual for the provider: a facility rate whose NPIs are all individual clinicians, anesthesia without an anesthesia clinician, a CT, MRI or PET read with no radiologist or nuclear medicine physician (cardiologists count for cardiac scans), deactivated NPIs. The files list contracted rates, not services performed, so a flag is a prompt to verify.

## Price history

Three monthly files are loaded. The snapshot picker in the app bar switches every page to another month, and the choice stays in the URL.

- **Changes** (`/changes`): the same provider (tax ID) in the same comparison group, between two files. The page says a rate went up or down (in dollars and percent), is new (only in the newer file) or is gone (only in the older one); the API calls these changed, new and dropped. Providers are matched on their tax ID key, never on `provider_group_id`, which the payer renumbers every month. A change means the file lists a different rate: a provider whose rates are the same, with only the NPI counts behind them moved, is unchanged. It isn't a claim, and the files don't say how often a service is used.
- **Facility charges with no procedure named** (filed under a revenue code alone) are left out of the order and the counts unless the switch the insights pages use adds them: hospitals bundle different services under them, so they don't compare like for like. The switch shows how many the other filters match. Added, they come after the rest in the default order.
- **Not before the numbers are saved**: changes read each month's saved statistics. Until both months' are built and current (after loading, or after a file or the app changed), the API answers `current: false` with a message and no counts or rows, and the page says the changes will show once loading finishes.
- **Relabeled** (listed differently): a quarter (July to August) to two fifths (August to September) of new and dropped rates are the same provider and code under other modifiers or another setting. Centene reshuffles telehealth modifiers (`95,GQ,GT` becomes `95,GT`). Counted as one rate gone and another new, they'd overstate churn, so the API and the page leave them out of the counts and rows by default, and Details says how many ("5,855 new and 5,853 gone are the same provider and service, relabeled."). A switch under More filters adds them back (`includeListedElsewhere=true`).
- **Trends**: each price tile says in a few words how its price moved across the files. A move that only reflects who is listed is never worded as a price change:
  - "Up $12 since Aug": an amount, only when the same providers are listed in every step.
  - "Rates up since Aug", "Rates down" or "Rates mixed": when providers also joined or left, so part of the move is who's listed.
  - "Rates steady since Jul": when every provider listed in both files kept its rate, even if the typical price moved. On the real 99213 modifier-25 tile, all 37 providers kept their rate while 87 left and 39 joined, and the typical price went from $51.45 to $114.

  Under Details, a chart shows the typical price, the range most providers get and, with an NPI or EIN, that provider's own rate. The numbers are listed month by month. The trend runs the search itself in each month, so every point equals that month's figures.
- **Partial uploads**: Centene has published partial months before (Ambetter files of 9 MB and 4 MB, against about 90 MB). Each product's file is compared with the same product in up to two months on each side: unique rates, EINs and codes, each against the nearby median. Below 0.8 on any of them, the file is flagged partial and left out of changes. The three loaded months all score 0.94 or more.

| Between | Changed (up, down) | New | Gone | Relabeled (new, gone) | Unchanged |
|---|---|---|---|---|---|
| 2026-07-27 and 2026-08-31 | 2,028 (1,231, 797) | 6,185 | 5,772 | 2,266, 2,097 | 46,158 |
| 2026-08-31 and 2026-09-28 | 1,326 (659, 667) | 8,250 | 10,207 | 5,855, 5,853 | 39,251 |

These are the page's defaults: relabeled rates and facility charges with no procedure named left out. With both added, July to August is 4,608 changed, 8,843 new and 8,232 gone, and August to September 3,899, 14,632 and 16,776.

These include facility charges with no procedure named (3,335 and 3,816 of the changed, new and dropped rows), which the page leaves out by default. Counts are of provider values: one per provider per comparison group. When a provider has several rates in a group, its value is their NPI-weighted median, so it also moves when a rate is added or removed; the changes page marks those rows. Same rates with only the NPI counts moved (11 and 33 pairs) are unchanged.

## Insights across codes

Three pages read each month's saved statistics, so they look across every code at once instead of searching one code at a time. Each says which month it describes and whether its numbers are up to date. Until the statistics are built, it says the numbers will fill in once loading finishes.

- **Price gaps** (`/insights/gaps`): comparison groups with 10 or more providers, ordered by how much less the cheapest quarter get than typical, or by the widest price range in dollars, one plan at a time or both.
  - On the 2026-08-31 files, 1,043 groups have 10+ providers.
  - In 727 of them the lower-priced quarter get the typical price, mostly because they are fee schedules. The page says how many it leaves out instead of ranking a $0 difference.
  - 125 are facility charges with no procedure named.
  - That leaves 191 to rank.
- **Providers** (`/providers`, then `/providers/:key`): one tax ID in every comparison group it is in, each placed exactly as a search with its EIN places it. A scorecard reached from a search says when it's listed under another name.
  - With 5 or more shared services, an overall line: "Usually about 8% more than typical (across 26 services)".
  - Lists of the services where it is lower or higher than most.
  - A masked tax ID can be found by its full number, but only its key comes back.
- **Compare plans** (`/insights/compare`): the same provider in the same comparison group under both plans. "The same" means exactly the same rate. Of the 7,951 provider-and-service pairs priced under both plans, 5,979 are paid exactly the same. Pairs priced in different units are left out and counted.
- Facility charges with no procedure named are left out of every reading and order by default. A switch adds them and shows how many it adds.
- On the real files, scorecard and comparison numbers were checked against search: 60 providers' scorecards, 150 ranked groups and 120 pairs each equal their searches.

## What each code is, visit levels and drug doses

Two more small CMS files ship in `reference/` (built by the same tool, see `reference/README.md`): the HCPCS Level II names from the October 2026 quarterly file, and the RBCS (Restructured BETOS) category of every CPT and HCPCS code. Neither holds CPT descriptors (the AMA's) or dental names (the ADA's).

- Each code gets **CMS's category**: "Procedures · Digestive/Gastrointestinal" for 45378, with the RBCS family in a tooltip. Codes RBCS doesn't list, like revenue codes, keep the code-range category, under one root: "Not in CMS's categories". `/api/v1/categories` lists the tree with code and rate counts (a subcategory's codes with no CMS family sit in "Other …", so children add up), and the code typeahead takes `category`.
- Two key sets, on purpose: a search result's `category` says how a code is priced (`drugs`, `anesthesia`: the unit), and its `classification` says what it is. `classification.key` (`PG004`, `PG`, `P`, `facility`) is what `/api/v1/categories` lists and `/codes?category=` takes, in any letter case; a key that isn't in the tree is a 400.
- When the plan's file doesn't name a code (a drug named only in a revenue-code rate's notes), the name comes from **CMS's HCPCS list**, and the page says so.
- **How it's priced** explains each way the prices are paid where it applies: anesthesia units, drugs per HCPCS unit, supplies per item, lab tests per test, facility rates under revenue codes, health centers paid per visit, imaging's reading and scan parts (modifiers 26 and TC), and visit levels.
- **Visit levels side by side**: office visits (99202–99205, 99211–99215) and ER visits (99281–99285) show each level's typical price on the same plan, setting, fee and modifiers, as a chart and a table. `/api/v1/rates/levels` builds it from the same numbers as each level's search.
- **What a dose comes to at the plan's rates**, for drugs: the unit comes from the code's name (J3301 is 10 mg; a name with two amounts, like "3 mg and 3 mg", gets no calculator), the dose is rounded up to whole units as billing does (any dose is at least 1), and the cost is units at the typical price per unit of the plan and bill on screen ("45 mg = 5 units → $4.75"). Discarded-drug (JW) and percentage prices never price a dose; when nothing else has 5 or more providers, the page says why instead.

## API endpoints

| Endpoint | |
|---|---|
| `GET /api/v1/rates?code=&npi=&ein=&provider=&specialty=&county=&product=&setting=&feeType=&group=&sort=&page=&pageSize=&snapshot=` | Comparison groups, provider rows, and hints that explain empty or surprising results. Every hint link is checked to find rates before it's sent. |
| `GET /api/v1/rates/{id}/source` · `/npis?provider=&alsoRate=` | Where a rate came from; the NPIs behind a result row, each with its rate |
| `GET /api/v1/codes?q=&category=` · `/providers?q=` · `/specialties?q=` · `/reference` | Typeahead and filter options (including New York counties and the registry release) |
| `GET /api/v1/insights/gaps?product=&category=&setting=&feeType=&code=&includeFacilityCharges=&sort=&snapshot=&page=&pageSize=` | Groups with 10+ providers, ranked by how much less the lower-priced quarter get, or by range |
| `GET /api/v1/insights/scorecard?provider=&includeFacilityCharges=&snapshot=` | One tax ID (EIN or provider key) in every comparison group it is in, with its overall reading |
| `GET /api/v1/insights/compare?product=&versus=&code=&category=&setting=&includeFacilityCharges=&sort=&snapshot=&page=&pageSize=` | The same provider in the same group under two plans |
| `GET /api/v1/categories` · `/rates/levels?code=` | Category tree (CMS RBCS, with code-range fallback) with code and rate counts; each visit level, like for like |
| `GET /api/v1/status` · `/status/stream` | Loading progress (the stream uses server-sent events) |
| `GET /api/v1/snapshots` · `/snapshots/{id}/plans` | Loaded files, their sources and counts, and how complete each looks; plans per product |
| `GET /api/v1/sources` | Every snapshot and file (rate and allowed-amount files): URL, three dates, size, SHA-256, ETag, counts, rejects by reason, timing |
| `GET /api/v1/out-of-network?code=&codeType=&snapshot=` | What the plan allowed out of network for a code, per product and fee type |
| `GET /api/v1/changes?from=&to=&product=&code=&kind=&minChangePct=&includeListedElsewhere=&includeFacilityCharges=&sort=&page=&pageSize=` | Rates that changed, appeared or dropped between two snapshots, with counts by kind. Defaults: the newest snapshot against the one before, biggest moves first, relabeled rates left out (`counts.newListedElsewhere` and `counts.droppedListedElsewhere` say how many `includeListedElsewhere=true` adds), facility charges with no procedure named left out (`counts.facilityCharges` says how many `includeFacilityCharges=true` adds, as on the insights endpoints). Until both snapshots' saved statistics are built and current: `current: false`, a `message`, `counts: null` and no rows. |
| `GET /api/v1/rates/trend?code=&npi=&ein=&provider=&specialty=&county=&product=&setting=&feeType=&snapshot=` | Each comparison group a search shows, in every snapshot: provider count, median and quartiles, the searched provider's rate, and (`sincePrevious`) how many providers stayed, got a different higher or lower rate, kept the same rates with their NPIs moved between them (`reweighted`), left or joined since the previous complete snapshot |
| `/health/live` · `/health/ready` | Ready once rate data can be searched |

## What I'd add next

- **% of Medicare** for each rate, the benchmark plans, employers and providers all use. The comparison groups and units are already in place.
- **CMS schema 3.0.** Network-level files arrive quarterly from April 2027 under the October 2026 final rule.
- **More payers and indexes.** The parser already handles gzip, string numbers, either key order and inline provider groups. With two payers, the plan comparison would work across payers as it does across plans.
- **A cost for a whole procedure**: facility, doctor and anesthesia units together. Codes, units and settings are already modeled; it needs a rule for which codes go together.
- **CSV export** of each insights page, and saved searches.
- **Refresh without a restart.** Each file is already saved in one transaction, so a scheduled re-check of the index could load a new month while the app keeps serving.
- **Inpatient (DRG) and employer-plan files**, which these Centene files don't include.

## How it was built and reviewed

This was built with Claude Code. I made the scope, data-handling and metric decisions; the AI wrote most of the code and tests. Every PR had to pass CI. Each core PR also went through adversarial reviews on the real files: independent reviewers recomputed every number from the database and followed every hint link, and an issue counted only if a second check reproduced it. The search API took six rounds (12, 17, 23, 34, 15 and 9 confirmed issues, the last two rounds with nothing high-severity). Those reviews caught real problems: an NPI search showing the tax ID's blended rate, masked tax IDs that could be reversed from their keys, a surgery center's Botox charge shown as a per-unit price, and hints that pointed to empty pages. Each now has a test.

Each extra (provider directory, plain numbers, sources and out-of-network, insights, price history, categories and doses) had one independent review on the real files, and every confirmed issue was fixed before it merged. The reviews caught:
- a typical price that moved only because providers joined or left, worded as a price change;
- a dose priced from the rate for discarded drug (JW);
- "the same" said of rates that weren't;
- facility charges ranked against procedures.

I added the plain-numbers rule after the core was built, and then cut every page down to one answer with the details folded away, because the people who use this aren't statisticians. The colonoscopy page went from 1,787 words to about 120.
