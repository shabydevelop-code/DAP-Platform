"""Convert only value-validation steps in the isolated 55-step Web clone.

Never changes the original Guide. Transition steps remain unchanged until
their before/after semantics have been verified.
"""
import argparse
import sqlite3
from pathlib import Path

DEFAULT_DB = r"C:\ProgramData\GuideMe\Data\GuideMe.db"
SOURCE = "sampleapp-web-guide"
DEST = "sampleapp-web-state-guide"
VALUE_STEPS = (1, 8, 9, 13, 14, 16, 17, 20, 27, 29, 30, 31, 34, 42, 43, 44, 45)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--db", default=DEFAULT_DB)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    path = Path(args.db)
    if not path.is_file():
        parser.error(f"Database not found: {path}")
    uri = path.resolve().as_uri() + ("?mode=rw" if args.apply else "?mode=ro")
    with sqlite3.connect(uri, uri=True, timeout=5) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("PRAGMA foreign_keys=ON")
        ids = dict(conn.execute("SELECT Key, Id FROM Guides WHERE Key IN (?, ?)", (SOURCE, DEST)))
        if len(ids) != 2 or ids[SOURCE] == ids[DEST]:
            parser.error("Both distinct source and destination Guides must exist")
        src = {r["StepOrder"]: r for r in conn.execute(
            "SELECT StepOrder, Key, ValidationKind, ValidationExpectedValue FROM GuideSteps WHERE GuideId=?",
            (ids[SOURCE],))}
        dest = {r["StepOrder"]: r for r in conn.execute(
            "SELECT Id, StepOrder, Key, ValidationKind, ValidationExpectedValue FROM GuideSteps WHERE GuideId=?",
            (ids[DEST],))}
        if len(src) != 55 or len(dest) != 55:
            parser.error("Expected exactly 55 steps in each Guide")
        changes = []
        for order in VALUE_STEPS:
            a, b = src[order], dest[order]
            if (a["Key"].replace("testcrm-", "sampleapp-", 1), a["ValidationExpectedValue"]) != (b["Key"], b["ValidationExpectedValue"]):
                parser.error(f"Step {order} differs from original; refusing conversion")
            expected = {"value-equals": "state-value-equals",
                        "value-not-empty": "state-value-not-empty"}.get(a["ValidationKind"])
            if expected is None:
                parser.error(f"Unexpected original validation at step {order}: {a['ValidationKind']}")
            if b["ValidationKind"] not in (a["ValidationKind"], expected):
                parser.error(f"Unexpected destination validation at step {order}: {b['ValidationKind']}")
            changes.append((order, b["Id"], b["ValidationKind"], expected))
        for order, _, before, after in changes:
            print(f"{order:2}: {before} -> {after}")
        if not args.apply:
            print("PREVIEW ONLY. Run --apply to update the cloned Guide.")
            return
        conn.execute("BEGIN IMMEDIATE")
        for _, step_id, _, kind in changes:
            conn.execute("UPDATE GuideSteps SET ValidationKind=? WHERE Id=?", (kind, step_id))
        conn.commit()
        print(f"UPDATED {len(changes)} value steps in {DEST}. Original untouched.")
        print("Transition steps are NOT converted; full Guide is not state-only yet.")

if __name__ == "__main__":
    main()
