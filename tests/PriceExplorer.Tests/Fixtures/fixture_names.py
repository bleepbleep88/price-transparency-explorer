"""Neutral placeholders for item names and descriptions the fixtures may not carry.

CPT descriptions are the AMA's and CDT descriptions the ADA's, and we have no license for either. Revenue-code items in
these files carry the same CPT text. So every item except a HCPCS Level II one (CMS's, public domain) gets "Item N" and
"Item N description" in place of its name and description. Each distinct published name gets its own placeholder, so
items that shared a name still do and items that didn't still don't (the tests count distinct item names).
"""
import re

HCPCS_LEVEL_TWO = re.compile(r"^[A-CE-V][0-9]{4}$", re.IGNORECASE)


def scrub(doc, items_key):
    names, descriptions = {}, {}
    for item in doc[items_key]:
        if HCPCS_LEVEL_TWO.match(str(item.get("billing_code", ""))):
            continue
        if "name" in item:
            item["name"] = names.setdefault(item["name"], f"Item {len(names) + 1}")
        if "description" in item:
            item["description"] = descriptions.setdefault(item["description"], f"Item {len(descriptions) + 1} description")
    return doc


if __name__ == "__main__":
    # Scrubs fixtures in place: python fixture_names.py <file.json>...
    import json
    import sys

    for path in sys.argv[1:]:
        doc = json.load(open(path, encoding="utf-8"))
        key = "in_network" if "in_network" in doc else "out_of_network"
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(scrub(doc, key), f, indent=2)
            f.write("\n")
        print(path, key, len(doc[key]), "items")
