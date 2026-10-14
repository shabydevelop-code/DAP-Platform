"""Convert reviewed Web modal/list transitions in the isolated state Guide.

Deliberately excludes steps without a proved observable postcondition.
Preview by default. The original Guide is never changed.
"""
import argparse
import json
import sqlite3
from pathlib import Path

DB = r"C:\ProgramData\GuideMe\Data\GuideMe.db"
SOURCE = "sampleapp-web-guide"
DEST = "sampleapp-web-state-guide"
# Each replacement is an observable DOM state, not a click/keyboard event.
SELECTORS = {
    6: "th[aria-sort='ascending'] button[data-sort='status']",
    18: "#ps-alert:not([hidden])",
    19: "#ps-alert[hidden]",
    21: "#save-success",
    32: "#ps-alert:not([hidden])",
    33: "#ps-alert[hidden]",
    35: "#save-success",
    36: "#ps-confirm:not([hidden])",
    37: "h2:has-text('לידים')",
    52: "#ps-confirm:not([hidden])",
    53: "h2:has-text('פניות')",
}
# Step 15 has no demonstrated UI state change and must remain click-based.
def main():
    p = argparse.ArgumentParser()
    p.add_argument("--db", default=DB)
    p.add_argument("--apply", action="store_true")
    args = p.parse_args()
    path = Path(args.db)
    if not path.is_file():
        p.error(f"Missing database: {path}")
    uri = path.resolve().as_uri() + ("?mode=rw" if args.apply else "?mode=ro")
    with sqlite3.connect(uri, uri=True, timeout=5) as db:
        db.row_factory = sqlite3.Row
        gids = dict(db.execute("SELECT Key, Id FROM Guides WHERE Key IN (?, ?)", (SOURCE, DEST)))
        if len(gids) != 2:
            p.error("Both Guides are required")
        def steps(gid):
            return {r["StepOrder"]: r for r in db.execute(
                "SELECT Id, StepOrder, Key, ValidationKind FROM GuideSteps WHERE GuideId=?", (gid,))}
        source, clone = steps(gids[SOURCE]), steps(gids[DEST])
        if len(source) != 55 or len(clone) != 55:
            p.error("Expected 55 steps per Guide")
        plans = []
        for order, selector in SELECTORS.items():
            a, b = source[order], clone[order]
            if a["ValidationKind"] != "clicked" or b["ValidationKind"] not in ("clicked", "state-observed"):
                p.error(f"Unexpected validation at step {order}")
            if b["Key"] not in (a["Key"], a["Key"].replace("testcrm-", "sampleapp-", 1)):
                p.error(f"Unexpected key at step {order}")
            conditions = list(db.execute(
                "SELECT rowid, Kind, TargetJson FROM StepCompletionConditions WHERE GuideStepId=? ORDER BY ConditionOrder", (b["Id"],)))
            if len(conditions) != 1 or conditions[0]["Kind"] != "target-exists":
                p.error(f"Unexpected conditions at step {order}")
            target = json.loads(conditions[0]["TargetJson"])
            # Selector is nested in TargetDescriptor; update only a known Locator structure.
            # Fail closed on unknown JSON shapes.
            locator = target.get("Locator")
            if not isinstance(locator, dict) or str(locator.get("Strategy", "")).lower() != "css":
                p.error(f"Unsupported CSS Locator at step {order}")
            old = locator.get("Value")
            if not isinstance(old, str):
                p.error(f"Missing CSS Locator.Value at step {order}")
            # Never modify FrameContext.Path or Anchors: those identify the
            # containing frame and fallback targets, not the condition itself.
            locator["Value"] = selector
            plans.append((order, b["Id"], conditions[0]["rowid"], json.dumps(target, ensure_ascii=False, separators=(",", ":"))))
            print(f"{order:2}: {b['ValidationKind']} -> state-observed; {old} -> {selector}")
        print("Step 15 excluded: no verified state transition.")
        if not args.apply:
            print("PREVIEW ONLY; use --apply after inspecting selector targets.")
            return
        db.execute("BEGIN IMMEDIATE")
        for order, step_id, rowid, target in plans:
            db.execute("UPDATE GuideSteps SET ValidationKind='state-observed' WHERE Id=?", (step_id,))
            db.execute("UPDATE StepCompletionConditions SET TargetJson=? WHERE rowid=?", (target, rowid))
        db.commit()
        print(f"UPDATED {len(plans)} transitions in cloned Guide only.")
        print("Runtime E2E verification is still required.")

if __name__ == "__main__":
    main()
