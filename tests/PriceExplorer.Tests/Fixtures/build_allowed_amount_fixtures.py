"""Slices Centene's 2026-08-31 allowed-amount (out-of-network) files into small test fixtures.

Usage: python build_allowed_amount_fixtures.py <ex_allowed-amounts.json> <es_allowed-amounts.json> <out_dir>
Prints the counts the tests check, computed here independently of the C# parser.
"""
import copy
import json
import statistics
import sys
from decimal import Decimal

src_ex, src_es, out = sys.argv[1:4]


def slice_file(path, picks):
    doc = json.load(open(path, encoding="utf-8"))
    items = []
    for pick in picks:
        matches = [it for it in doc["out_of_network"] if it["billing_code_type"] == pick["type"] and it["billing_code"] == pick["code"]]
        items.extend(copy.deepcopy(it) for it in matches[: pick.get("items", 1)])
    sliced = {k: v for k, v in doc.items() if k != "out_of_network"}
    sliced["out_of_network"] = items
    return sliced


def canonical(code_type, code):
    if code.isdigit():
        if code_type == "CPT":
            return code.zfill(5)
        if code_type == "RC":
            return code.zfill(4)
    return code


def ein_of(value):
    digits = "".join(c for c in str(value) if c.isdigit())
    return "0" + digits if len(digits) == 8 and str(value).isdigit() else digits


def stats(doc):
    published = 0
    unique = {}
    for it in doc["out_of_network"]:
        code = (it["billing_code_type"], canonical(it["billing_code_type"], it["billing_code"]))
        for entry in it["allowed_amounts"]:
            tin = (entry["tin"]["type"], ein_of(entry["tin"]["value"]))
            for p in entry["payments"]:
                published += 1
                providers = tuple(sorted((str(pr["billed_charge"]), tuple(sorted(pr["npi"]))) for pr in p["providers"]))
                key = (code, tin, tuple(sorted(entry["service_code"])), entry["billing_class"], p["allowed_amount"],
                       tuple(sorted(p.get("billing_code_modifier") or [])), providers)
                unique.setdefault(key, (p, entry))
    print(f"  items={len(doc['out_of_network'])} published_payments={published} unique_payments={len(unique)}")
    by_code = {}
    for key, (p, entry) in unique.items():
        by_code.setdefault(key[0], []).append((key, p))
    for code, rows in sorted(by_code.items()):
        allowed = sorted(Decimal(str(p["allowed_amount"])) for _, p in rows)
        billed = sorted(Decimal(str(pr["billed_charge"])) for _, p in rows for pr in p["providers"])
        tins = {key[1] for key, _ in rows}
        npis = {n for _, p in rows for pr in p["providers"] for n in pr["npi"]}
        median = lambda xs: statistics.median(xs) if len(xs) >= 5 else None
        print(f"  {code}: payments={len(rows)} tax_ids={len(tins)} npis={len(npis)} "
              f"allowed min={allowed[0]} median={median(allowed)} max={allowed[-1]} "
              f"billed n={len(billed)} min={billed[0]} median={median(billed)} max={billed[-1]}")


ex = slice_file(src_ex, [
    {"type": "RC", "code": "450", "items": 2},  # the same 12 payments under two item names
    {"type": "CPT", "code": "74177"},           # professional, one payment listing four places of service
    {"type": "CPT", "code": "812"},             # anesthesia published without leading zeros
    {"type": "HCPCS", "code": "A0425"},         # ambulance mileage, nine tax IDs
])
es = slice_file(src_es, [
    {"type": "CPT", "code": "142"},             # an EIN published as 8 digits (leading zero lost)
    {"type": "RC", "code": "450", "items": 2},  # 20 payments under two item names, two 8-digit EINs
    {"type": "CPT", "code": "74177"},
    {"type": "CPT", "code": "812"},
])

for name, doc in (("2026-08-31_fidelis-ex_allowed-amounts.json", ex), ("2026-08-31_fidelis-es_allowed-amounts.json", es)):
    with open(f"{out}/{name}", "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, indent=2)
        f.write("\n")
    print(name)
    stats(doc)
