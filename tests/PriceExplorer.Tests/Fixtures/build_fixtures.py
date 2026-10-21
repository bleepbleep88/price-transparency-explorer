import json, sys, copy
from fixture_names import scrub
src_ex, src_es, src_index, out = sys.argv[1:5]

def slice_file(path, picks, extra_ref_predicates, max_blocks=4):
    d = json.load(open(path, encoding="utf-8"))
    refs = {r["provider_group_id"]: r for r in d["provider_references"]}
    items_out, used = [], set()
    for pick in picks:
        matches = [it for it in d["in_network"] if it["billing_code_type"] == pick["type"] and it["billing_code"] == pick["code"]]
        for it in matches[: pick.get("items", 1)]:
            it = copy.deepcopy(it)
            blocks = []
            for nr in it["negotiated_rates"]:
                prices = nr["negotiated_prices"]
                if "ai" in pick:
                    prices = [p for p in prices if p.get("additional_information") in pick["ai"]]
                if not prices: continue
                nr["negotiated_prices"] = prices
                blocks.append(nr)
                if len(blocks) >= pick.get("blocks", max_blocks): break
            if not blocks: continue
            it["negotiated_rates"] = blocks
            for nr in blocks: used.update(nr["provider_references"])
            items_out.append(it)
    extra = []
    for pred in extra_ref_predicates:
        for r in d["provider_references"]:
            if r["provider_group_id"] not in used and pred(r):
                extra.append(r["provider_group_id"]); break
    kept = sorted(used | set(extra))
    out_doc = {k: v for k, v in d.items() if k not in ("provider_references", "in_network")}
    out_doc["provider_references"] = [refs[i] for i in kept if i in refs]
    out_doc["in_network"] = items_out
    return out_doc

ein_level = lambda r: any(g["npi"] == [0] for g in r["provider_groups"])
nine_digit = lambda r: any(len(str(n)) == 9 for g in r["provider_groups"] for n in g["npi"])
unissued = lambda r: any(g["tin"]["value"][:2] in ("07", "08", "09") for g in r["provider_groups"])
bad_check = lambda r: any(n == 1861171663 for g in r["provider_groups"] for n in g["npi"])

ex = slice_file(src_ex, [
    {"type": "RC", "code": "490", "items": 2, "ai": {"29881", "45378"}, "blocks": 4},
    {"type": "CPT", "code": "45378", "blocks": 8},
    {"type": "CPT", "code": "812"},
    {"type": "RC", "code": "450", "ai": {"99284"}, "blocks": 3},
    {"type": "HCPCS", "code": "J0585"},
    {"type": "CPT", "code": "99214", "blocks": 3},
], [ein_level, nine_digit, unissued])
es = slice_file(src_es, [
    {"type": "RC", "code": "490", "items": 1, "ai": {"29881", "45378"}, "blocks": 3},
    {"type": "CPT", "code": "45378", "blocks": 4},
], [bad_check])

index = json.load(open(src_index, encoding="utf-8"))
for i, s in enumerate(index["reporting_structure"]):
    s["reporting_plans"] = s["reporting_plans"][:4]
    name = "ex" if i == 0 else "es"
    s["in_network_files"] = [{"description": "in-network file", "location": f"https://fixtures.test/2026-08-31_fidelis-{name}_in-network.json"}]
    s["allowed_amount_file"] = {"description": "allowed-amounts file", "location": f"https://fixtures.test/2026-08-31_fidelis-{name}_allowed-amounts.json"}

# No CPT, CDT or revenue-code item text in the fixtures: see fixture_names.py.
scrub(ex, "in_network"); scrub(es, "in_network")
for name, doc in (("2026-08-31_fidelis_index.json", index), ("2026-08-31_fidelis-ex_in-network.json", ex), ("2026-08-31_fidelis-es_in-network.json", es)):
    with open(f"{out}/{name}", "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, indent=2)
        f.write("\n")

def stats(doc):
    prices = sum(len(nr["negotiated_prices"]) for it in doc["in_network"] for nr in it["negotiated_rates"])
    return f"refs={len(doc['provider_references'])} items={len(doc['in_network'])} prices={prices}"
print("ex:", stats(ex)); print("es:", stats(es))
