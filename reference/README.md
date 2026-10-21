# Reference data

Small files the API ships with. Each is built from public sources by `tools/PriceExplorer.ReferenceBuilder` and loaded into DuckDB at startup.

## provider_directory.csv.gz

Name, specialty, and practice location for every NPI that appears in the loaded rate files (the rate files themselves only list bare NPI numbers).

| Column | Source |
|---|---|
| `npi`, `entity_type`, `name`, `credential`, `city`, `state`, `zip5`, `deactivated`, `nppes_last_updated` | CMS NPPES monthly file (`NPPES_Data_Dissemination_<Month>_<Year>_V2.zip`) |
| `taxonomy_code` | The provider's primary taxonomy in NPPES |
| `specialty`, `specialty_group` | CMS's Medicare Provider and Supplier Taxonomy Crosswalk: the Medicare specialty for the NPI's taxonomy code (`specialty_group` is "Physician" for physician specialties) |
| `county_fips`, `county`, `borough` | Census 2020 ZCTA-to-county relationship file (the county holding most of the ZIP's land area; NYC counties named as boroughs). A ZIP the file lacks (a PO box or a building's own ZIP), or one in another state than the address, gives way to the county that 90% or more of the same city's NPIs share. Practices outside the US get none. |

Street addresses and phone numbers are left out on purpose. `provider_directory.source.json` records the exact source URLs, when it was built, and how many NPIs matched.

Rebuild after loading new snapshots (downloads ~1.1 GB once into `.data/reference-cache`, streams the registry without unzipping it, takes a few minutes):

```bash
dotnet run --project tools/PriceExplorer.ReferenceBuilder -c Release -- providers --database .data/explorer.duckdb
```

Caveats: NPPES addresses are self-reported and can be stale or out of state; a provider's specialty is its primary taxonomy only; a deactivated NPI keeps only its number and dates (no name, type, specialty or place).

**Specialty names.** Only CMS's columns of the crosswalk are used (Medicare specialty code and description, public domain), never its taxonomy descriptions, which are the NUCC's. CMS lists some codes under several specialties; the builder takes the most specific, with a few picks written in `MedicareSpecialties.cs`. Codes CMS doesn't list (Medicare doesn't enroll every provider type, about 5% of NPIs) read "Other physician" or "Other provider type". If the NPPES download page is down, `--nppes <zip>` builds from a monthly file already downloaded.

## hcpcs_names.csv.gz

`code`, `description`: the long name of every HCPCS Level II code (a letter and four digits, such as J0585) in CMS's newest quarterly HCPCS file (October 2026). The search shows it when the plan's file doesn't name the code itself. Public domain. Two kinds of names are left out on purpose: CPT codes (the AMA's descriptors, and not in this file anyway) and dental D codes (the American Dental Association's). The app shows no CPT or CDT descriptions at all: see the main README's Notice.

## rbcs.csv.gz

`code`, `rbcs_id`, `category_code`, `category`, `subcategory_code`, `subcategory`, `family_code`, `family`: CMS's Restructured BETOS Classification System (RBCS, RY2026) for every CPT and HCPCS code, one row per code (the assignment CMS marks as the latest; `family` is empty where CMS gives none). Codes and CMS's category names only, no code descriptors. Codes it doesn't cover (revenue codes, some new codes) fall back to the code-range rules in `ServiceCategories.cs`.

Rebuild both (about 3 MB of downloads into `.data/reference-cache`; no database needed):

```bash
dotnet run --project tools/PriceExplorer.ReferenceBuilder -c Release -- codes
```

Each file's `.source.json` names the exact download and release.
