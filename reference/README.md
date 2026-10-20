# Reference data

Small files the API ships with. Each is built from public sources by `tools/PriceExplorer.ReferenceBuilder` and loaded into DuckDB at startup.

## provider_directory.csv.gz

Name, specialty, and practice location for every NPI that appears in the loaded rate files (the rate files themselves only list bare NPI numbers).

| Column | Source |
|---|---|
| `npi`, `entity_type`, `name`, `credential`, `city`, `state`, `zip5`, `deactivated`, `nppes_last_updated` | CMS NPPES monthly file (`NPPES_Data_Dissemination_<Month>_<Year>_V2.zip`) |
| `taxonomy_code` | The provider's primary taxonomy in NPPES |
| `specialty`, `specialty_group` | NUCC Health Care Provider Taxonomy code set (display name and grouping) |
| `county_fips`, `county`, `borough` | Census 2020 ZCTA-to-county relationship file (the county holding most of the ZIP's land area; NYC counties named as boroughs) |

Street addresses and phone numbers are left out on purpose. `provider_directory.source.json` records the exact source URLs, when it was built, and how many NPIs matched.

Rebuild after loading new snapshots (downloads ~1.1 GB once into `.data/reference-cache`, streams the registry without unzipping it, takes a few minutes):

```bash
dotnet run --project tools/PriceExplorer.ReferenceBuilder -c Release -- providers --database .data/explorer.duckdb
```

Caveats: NPPES addresses are self-reported and can be stale or out of state; a provider's specialty is its primary taxonomy only; a deactivated NPI keeps only its number and dates (no name, type, specialty or place).
