# Test fixtures

Small slices of Centene's real **2026-08-31 Fidelis** Transparency in Coverage files, so tests run on real data shapes with no network.

| Fixture | Sliced from | Source SHA-256 |
|---|---|---|
| `2026-08-31_fidelis_index.json` | `https://www.centene.com/content/dam/centene/Centene%20Corporate/json/DOCUMENT/2026-08-31_fidelis_index.json` | `bb3a34c9f4194bc5e4e04e612b71979aa939eb3c15ca49e8f22774d8b9967f84` |
| `2026-08-31_fidelis-ex_in-network.json` | `.../2026-08-31_centene-management-company-llc_fidelis-ex_in-network.json` (Ambetter) | `0d0afd0fbf14a544004d4dc797a357257e0677bf36330e72673c1c0fbcc10214` |
| `2026-08-31_fidelis-es_in-network.json` | `.../2026-08-31_centene-management-company-llc_fidelis-es_in-network.json` (Essential Plan) | `870d9568a8c2fafcc72d86125fb8adc663a4cdac17f27fd2418b472b385a8839` |
| `2026-08-31_fidelis-ex_allowed-amounts.json` | `.../2026-08-31_centene-management-company-llc_fidelis-ex_allowed-amounts.json` (Ambetter, out of network) | `9a6a7a4b0f85b0f88da1cd16dd23d2d883de2d41810e3643d141b7ae79aae5ff` |
| `2026-08-31_fidelis-es_allowed-amounts.json` | `.../2026-08-31_centene-management-company-llc_fidelis-es_allowed-amounts.json` (Essential Plan, out of network) | `937d346f18ba6da9bac1b4eaa89a380159c6d6ff71a1c6720185f8726e11d2b1` |

What's kept (see `build_fixtures.py`):

- Index: both reporting structures, the first 4 plans of each, file locations pointed at `https://fixtures.test/`.
- Ambetter: two revenue-code 490 items carrying CPT 29881 / 45378 in `additional_information` (the same rate blocks under different item names, so duplicates collapse), CPT 45378 professional rates with modifiers, anesthesia CPT `812` (published without leading zeros), revenue code 450 with ER level 99284, per-unit drug J0585, CPT 99214; plus every provider reference those use, a whole-TIN contract (`npi: [0]`), a 9-digit NPI, and a tax ID with a prefix the IRS never issues.
- Essential Plan: one revenue-code 490 item and CPT 45378, plus the reference holding NPI 1861171663 (fails the check digit).
- Allowed amounts, whole items (see `build_allowed_amount_fixtures.py`): Ambetter's first two revenue-code 450 items (the same
  12 payments under two item names, so duplicates collapse), CPT 74177 (one tax ID, four places of service), anesthesia
  CPT `812` and ambulance mileage A0425; Essential Plan's CPT `142` (EIN published as 8 digits, `43431956`), its first two
  revenue-code 450 items (two more 8-digit EINs), CPT 74177 and CPT `812`. Ambetter: 39 published payments, 27 unique;
  Essential Plan: 68 and 48.

Rebuild (Python 3, from downloaded source files):

```bash
python build_fixtures.py <ex_in-network.json> <es_in-network.json> <fidelis_index.json> .
python build_allowed_amount_fixtures.py <ex_allowed-amounts.json> <es_allowed-amounts.json> .
```

The second script also prints the counts the tests check (payments, tax IDs, NPIs, min/median/max per code), computed
independently of the C# parser.
