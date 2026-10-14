"""Read-only completion contract audit for the two ORIGINAL GuideMe guides.

Outputs a per-step CSV and console summary. Never modifies the database.
"""
import argparse
import csv
import json
import sqlite3
from collections import Counter
from pathlib import Path

GUIDES = ("sampleapp-web-guide", "sampleapp-windows-guide")

def describe_target(raw):
    if not raw:
        return ""
    try:
        data = json.loads(raw)
        if not isinstance(data, dict):
            return str(data)
        locator = data.get("Locator") or {}
        frame = data.get("FrameContext") or {}
        return json.dumps({
            "locator": locator,
            "frame": frame.get("Path"),
            "anchors": len(data.get("Anchors") or [])
        }, ensure_ascii=False, separators=(",", ":"))
    except (ValueError, TypeError):
        return str(raw)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--db", default=r"C:\ProgramData\GuideMe\Data\GuideMe.db")
    parser.add_argument("--output", default="guide-completion-audit.csv")
    args = parser.parse_args()
    path = Path(args.db)
    if not path.is_file():
        parser.error(f"Database not found: {path}")
    rows = []
    with sqlite3.connect(path.resolve().as_uri() + "?mode=ro", uri=True, timeout=5) as db:
        db.row_factory = sqlite3.Row
        for key in GUIDES:
            guide = db.execute("SELECT Id FROM Guides WHERE Key=?", (key,)).fetchone()
            if guide is None:
                parser.error(f"Missing original guide: {key}")
            steps = db.execute("""
                SELECT Id, StepOrder, Key, ValidationKind, ValidationExpectedValue,
                       AdvanceMode, TargetDescriptor
                FROM GuideSteps WHERE GuideId=? ORDER BY StepOrder
            """, (guide["Id"],)).fetchall()
            if len(steps) != 55:
                parser.error(f"Expected 55 steps for {key}, found {len(steps)}")
            for step in steps:
                conditions = db.execute("""
                    SELECT Kind, TargetJson FROM StepCompletionConditions
                    WHERE GuideStepId=? ORDER BY ConditionOrder
                """, (step["Id"],)).fetchall()
                kind = step["ValidationKind"] or ""
                rows.append({
                    "Guide": key,
                    "Step": step["StepOrder"],
                    "Key": step["Key"],
                    "AdvanceMode": step["AdvanceMode"],
                    "ValidationKind": kind,
                    "ExpectedValue": step["ValidationExpectedValue"] or "",
                    "ActionTarget": describe_target(step["TargetDescriptor"]),
                    "CompletionConditionCount": len(conditions),
                    "CompletionConditions": json.dumps([
                        {"kind": c["Kind"], "target": describe_target(c["TargetJson"])}
                        for c in conditions
                    ], ensure_ascii=False),
                    "ReviewFlag": (
                        "CLICK_WITHOUT_POSTCONDITION" if kind.lower() == "clicked" and not conditions
                        else "CLICK_EVENT_DEPENDENCY" if kind.lower() == "clicked"
                        else "TEXT_COMMIT_PRESERVE" if kind.lower() in ("value-equals", "value-not-empty")
                        else "MANUAL_OR_OTHER"
                    ),
                })
    output = Path(args.output)
    with output.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    for key in GUIDES:
        items = [r for r in rows if r["Guide"] == key]
        counts = Counter(r["ReviewFlag"] for r in items)
        print(f"{key}: {len(items)} steps; " + ", ".join(f"{k}={v}" for k,v in sorted(counts.items())))
        for r in items:
            if r["ReviewFlag"] == "CLICK_WITHOUT_POSTCONDITION":
                print(f"  NEEDS POSTCONDITION: step {r['Step']} {r['Key']}")
    print(f"Read-only report: {output.resolve()}")
    print("NOTE: Conditions are listed, not verified against live application state.")

if __name__ == "__main__":
    main()
