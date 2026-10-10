#!/usr/bin/env python3
"""Read-only audit of guide target search scope. Requires Python 3, no packages."""
import argparse
import csv
import sqlite3
import sys

def classify(strategy, anchors):
    strategy = (strategy or "").lower()
    scoped = [a for a in anchors if a[0].lower() in ("ancestor", "context")]
    exact_scopes = [a for a in scoped if a[1].lower() in ("automation-id", "name")]
    exact_descendants = [a for a in anchors if a[0].lower() == "descendant" and a[1].lower() == "name-regex"
                         and a[2].startswith("^") and a[2].endswith("$")]
    if exact_scopes and exact_descendants:
        return "scope-first", "Anchored scope and exact descendant; may still enumerate scoped rows"
    if not anchors and strategy in ("automation-id", "name"):
        return "root-fast", "Exact locator, root-level FindFirst; subtree may include grids"
    if not anchors:
        return "root-broad", "No anchors; potentially broad root search"
    return "root-filter", "Primary candidates collected before anchor filtering"

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--db", required=True, help="Path to DAP.db")
    parser.add_argument("--guide", required=True, help="Guide key")
    parser.add_argument("--output", default="guide-target-audit.csv")
    args = parser.parse_args()
    connection = sqlite3.connect("file:" + args.db.replace("\\", "/") + "?mode=ro", uri=True)
    rows = connection.execute("""
        SELECT s.Id,s.StepOrder,s.Key,s.Runtime,s.LocatorStrategy,s.LocatorValue,s.IsEnabled
        FROM GuideSteps s JOIN Guides g ON g.Id=s.GuideId
        WHERE g.Key=? ORDER BY s.StepOrder
    """, (args.guide,)).fetchall()
    if not rows:
        sys.exit("No steps found for this guide key.")
    fields = ["order", "step", "runtime", "enabled", "strategy", "locator", "anchors",
              "search_path", "risk_note"]
    counts = {}
    with open(args.output, "w", encoding="utf-8-sig", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=fields)
        writer.writeheader()
        for step_id, order, key, runtime, strategy, locator, enabled in rows:
            anchors = connection.execute("""
                SELECT Relation,LocatorStrategy,LocatorValue
                FROM TargetAnchors WHERE GuideStepId=? ORDER BY AnchorOrder
            """, (step_id,)).fetchall()
            path, note = classify(strategy, anchors)
            counts[path] = counts.get(path, 0) + 1
            writer.writerow(dict(order=order, step=key, runtime=runtime, enabled=enabled,
                                 strategy=strategy, locator=locator,
                                 anchors=" | ".join(":".join(map(str, a)) for a in anchors),
                                 search_path=path, risk_note=note))
    print(f"Audited {len(rows)} steps in {args.guide}; output: {args.output}")
    for key, value in sorted(counts.items()):
        print(f"  {key}: {value}")
    print("Classification is static: it does not prove uniqueness or actual UIA timing.")

if __name__ == "__main__":
    main()
