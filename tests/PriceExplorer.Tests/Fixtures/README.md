# Test fixtures

Small slices of Centene's real **2026-08-31 Fidelis** Transparency in Coverage files, so tests run on real data shapes with no network.

| Fixture | Sliced from | Source SHA-256 |
|---|---|---|
| `2026-08-31_fidelis_index.json` | `https://www.centene.com/content/dam/centene/Centene%20Corporate/json/DOCUMENT/2026-08-31_fidelis_index.json` | `bb3a34c9f4194bc5e4e04e612b71979aa939eb3c15ca49e8f22774d8b9967f84` |
| `2026-08-31_fidelis-ex_in-network.json` | `.../2026-08-31_centene-management-company-llc_fidelis-ex_in-network.json` (Ambetter) | `0d0afd0fbf14a544004d4dc797a357257e0677bf36330e72673c1c0fbcc10214` |
| `2026-08-31_fidelis-es_in-network.json` | `.../2026-08-31_centene-management-company-llc_fidelis-es_in-network.json` (Essential Plan) | `870d9568a8c2fafcc72d86125fb8adc663a4cdac17f27fd2418b472b385a8839` |

What's kept (see `build_fixtures.py`):

- Index: both reporting structures, the first 4 plans of each, file locations pointed at `https://fixtures.test/`.
- Ambetter: two revenue-code 490 items carrying CPT 29881 / 45378 in `additional_information` (the same rate blocks under different item names, so duplicates collapse), CPT 45378 professional rates with modifiers, anesthesia CPT `812` (published without leading zeros), revenue code 450 with ER level 99284, per-unit drug J0585, CPT 99214; plus every provider reference those use, a whole-TIN contract (`npi: [0]`), a 9-digit NPI, and a tax ID with a prefix the IRS never issues.
- Essential Plan: one revenue-code 490 item and CPT 45378, plus the reference holding NPI 1861171663 (fails the check digit).

Rebuild (Python 3, from downloaded source files):

```bash
python build_fixtures.py <ex_in-network.json> <es_in-network.json> <fidelis_index.json> .
```
