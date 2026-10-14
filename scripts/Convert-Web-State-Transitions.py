"""Convert a reviewed subset of Web transitions to state observation.

Only the isolated cloned Guide is changed. All other transitions remain on
click validation until their postconditions can be proved distinct.
"""
import argparse
import sqlite3
from pathlib import Path

DB = r"C:\ProgramData\GuideMe\Data\GuideMe.db"
SOURCE = "sampleapp-web-guide"
DEST = "sampleapp-web-state-guide"
# Destination differs from the source page, with no reused modal or saved-record marker.
ORDERS = (2, 3, 4, 5, 7, 10, 11, 12, 22, 23, 24, 25, 26, 28, 38, 39, 40, 41, 46, 47, 48, 49, 50, 54)

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
        guides = dict(conn.execute("SELECT Key, Id FROM Guides WHERE Key IN (?, ?)", (SOURCE, DEST)))
        if len(guides) != 2:
            parser.error("Source and destination Guides are required")
        def steps(gid):
            return {r["StepOrder"]: r for r in conn.execute(
                "SELECT Id, StepOrder, Key, ValidationKind, AdvanceMode FROM GuideSteps WHERE GuideId=?",
                (gid,))}
        original, clone = steps(guides[SOURCE]), steps(guides[DEST])
        if len(original) != 55 or len(clone) != 55:
            parser.error("Both Guides must contain 55 steps")
        for order in ORDERS:
            src, dst = original[order], clone[order]
            if src["Key"] != dst["Key"] or src["ValidationKind"] != "clicked" or dst["ValidationKind"] not in ("clicked", "state-observed"):
                parser.error(f"Unexpected Guide step {order}; aborting")
            source_conditions = conn.execute(
                "SELECT Kind, ExpectedValue, TargetJson FROM StepCompletionConditions WHERE GuideStepId=? ORDER BY ConditionOrder",
                (src["Id"],)).fetchall()
            clone_conditions = conn.execute(
                "SELECT Kind, ExpectedValue, TargetJson FROM StepCompletionConditions WHERE GuideStepId=? ORDER BY ConditionOrder",
                (dst["Id"],)).fetchall()
            if [tuple(r) for r in source_conditions] != [tuple(r) for r in clone_conditions]:
                parser.error(f"Condition mismatch at step {order}")
            if len(clone_conditions) != 1 or clone_conditions[0]["Kind"] != "target-exists":
                parser.error(f"Unsupported completion condition at step {order}")
            print(f"{order:2} {src['Key']}: {dst['ValidationKind']} -> state-observed")
        if not args.apply:
            print("PREVIEW ONLY. Use --apply to convert the listed transitions.")
            return
        conn.execute("BEGIN IMMEDIATE")
        for order in ORDERS:
            conn.execute("UPDATE GuideSteps SET ValidationKind='state-observed' WHERE Id=?", (clone[order]["Id"],))
        conn.commit()
        print(f"UPDATED {len(ORDERS)} transitions in {DEST}. Original Guide untouched.")
        print("Remaining click steps require individually verified transition semantics.")

if __name__ == "__main__":
    main()
