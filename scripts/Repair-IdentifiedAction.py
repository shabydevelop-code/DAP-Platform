"""Repair one identified action's unstable primary locator, with strict guards.

The selector must be chosen from the target application's actual DOM, not guessed.
Only changes the primary Element's LocatorsJson; anchors and variables stay intact.
"""
import argparse
import json
import sqlite3

p = argparse.ArgumentParser()
p.add_argument("--db", default=r"C:\ProgramData\GuideMe\Data\GuideMe-V5.db")
p.add_argument("--guide", type=int, required=True)
p.add_argument("--step", required=True)
p.add_argument("--expected", required=True, help="Existing CSS selector (optimistic concurrency guard)")
p.add_argument("--selector", required=True, help="Stable CSS selector scoped by SharedContainer")
p.add_argument("--apply", action="store_true")
args = p.parse_args()

if not args.selector.strip() or args.selector.startswith("#"):
    p.error("Supply a stable scoped action selector, not an element ID")
if any(token in args.selector for token in ("{{", ":exact-text(", ":cell-text(")):
    p.error("The action selector must not contain variables or extended selectors")

with sqlite3.connect(args.db) as db:
    db.row_factory = sqlite3.Row
    target = db.execute("""SELECT e.ElementId,e.LocatorsJson
        FROM StepTargets t JOIN Elements e ON e.GuideId=t.GuideId AND e.ElementId=t.ElementId
        WHERE t.GuideId=? AND t.StepId=? AND t.Purpose='Primary'""",
        (args.guide,args.step)).fetchall()
    if len(target)!=1:
        raise SystemExit("Expected exactly one primary target")
    element_id = target[0]["ElementId"]
    old = json.loads(target[0]["LocatorsJson"])
    if len(old)!=1 or old[0].get("Strategy")!="css" or old[0].get("Value")!=args.expected:
        raise SystemExit("Primary selector differs from expected; no changes made")
    anchor_count = db.execute("""SELECT COUNT(*) FROM ElementAnchors
        WHERE GuideId=? AND ElementId=? AND Relation='SharedContainer'""",
        (args.guide,element_id)).fetchone()[0]
    if anchor_count!=1:
        raise SystemExit("Expected one SharedContainer anchor; no changes made")
    new = json.dumps([{"Strategy":"css","Value":args.selector}],ensure_ascii=False)
    print("Guide:",args.guide,"Step:",args.step)
    print("Before:",args.expected)
    print("After: ",args.selector)
    if not args.apply:
        print("DRY RUN: add --apply to update")
    else:
        db.execute("""UPDATE Elements SET LocatorsJson=? WHERE GuideId=? AND ElementId=? AND LocatorsJson=?""",
                   (new,args.guide,element_id,target[0]["LocatorsJson"]))
        if db.total_changes!=1:
            raise SystemExit("Concurrent change detected; no update")
        db.commit()
        print("UPDATED: primary locator only; anchor preserved")
