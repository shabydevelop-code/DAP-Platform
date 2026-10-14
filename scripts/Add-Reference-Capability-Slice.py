"""Add verified-source locator and anchor variants to the independent reference guide.

This is the FIRST capability slice, not the final full-coverage guide.
Read-only by default. --apply is transactional and refuses modified targets.
"""
import argparse
import sqlite3
from pathlib import Path

DB = r"C:\ProgramData\GuideMe\Data\GuideMe.db"
GUIDE = "sampleapp-reference-guide"
# Exact keys/locators checked against the persisted mixed-guide export.
PROBES = (
    ("web-testcrm-customer-name", "Web", "css", "[name='name']", "label", "שם לקוח",
     (("Ancestor", "css", "#customer-search"), ("Ancestor", "css", "label.field"))),
    ("web-testcrm-customer-search-button", "Web", "css", "#customer-search button.primary", "role", "button",
     (("Ancestor", "css", "#customer-search"), ("Self", "css", "button.primary"))),
    ("windows-testcrm-windows-customer-name", "Windows", "automation-id", "CustomerNameSearch",
     "automation-id", "CustomerNameSearch", (("Self", "automation-id", "CustomerNameSearch"),)),
)
# Web Self is unsupported: replace with supported Context/Ancestor.
PROBES = tuple((key, runtime, old_strategy, old_value, new_strategy, new_value,
                tuple(("Context" if runtime == "Web" and relation == "Self" else relation,
                       strategy, value) for relation, strategy, value in anchors))
               for key, runtime, old_strategy, old_value, new_strategy, new_value, anchors in PROBES)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--db", default=DB)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    path = Path(args.db)
    if not path.is_file():
        parser.error(f"Database missing: {path}")
    uri = path.resolve().as_uri() + ("?mode=rw" if args.apply else "?mode=ro")
    with sqlite3.connect(uri, uri=True, timeout=5) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("PRAGMA busy_timeout=5000")
        guide = conn.execute("SELECT Id FROM Guides WHERE Key=?", (GUIDE,)).fetchone()
        if guide is None:
            raise ValueError("Reference guide missing")
        count = conn.execute("SELECT COUNT(*) FROM GuideSteps WHERE GuideId=?", (guide["Id"],)).fetchone()[0]
        if count != 6:
            raise ValueError(f"Expected six-step baseline, found {count}; refusing to modify")
        prepared = []
        for key, runtime, old_strategy, old_value, new_strategy, new_value, anchors in PROBES:
            row = conn.execute(
                "SELECT Id,Runtime,LocatorStrategy,LocatorValue FROM GuideSteps WHERE GuideId=? AND Key=?",
                (guide["Id"], key)).fetchone()
            if row is None or (row["Runtime"], row["LocatorStrategy"], row["LocatorValue"]) != (
                    runtime, old_strategy, old_value):
                raise ValueError(f"Unexpected step definition: {key}")
            existing = conn.execute("SELECT COUNT(*) FROM TargetAnchors WHERE GuideStepId=?", (row["Id"],)).fetchone()[0]
            if existing:
                raise ValueError(f"Anchors already exist on {key}; refusing overwrite")
            prepared.append((row["Id"], key, new_strategy, new_value, anchors))
            print(f"{key}: {old_strategy} -> {new_strategy}; {len(anchors)} anchors")
        if not args.apply:
            print("PREVIEW ONLY. No changes made.")
            return
        conn.execute("BEGIN IMMEDIATE")
        for step_id, key, strategy, value, anchors in prepared:
            conn.execute("UPDATE GuideSteps SET LocatorStrategy=?, LocatorValue=? WHERE Id=?",
                         (strategy, value, step_id))
            for order, (relation, anchor_strategy, anchor_value) in enumerate(anchors):
                conn.execute(
                    "INSERT INTO TargetAnchors(GuideStepId,AnchorOrder,Relation,LocatorStrategy,LocatorValue) VALUES(?,?,?,?,?)",
                    (step_id, order, relation, anchor_strategy, anchor_value))
        conn.commit()
        print("Installed first reference capability slice. Runtime verification required.")


if __name__ == "__main__":
    main()
