#!/usr/bin/env python3
"""Read-only GuideMe Instructor coverage audit. Python stdlib only; no DB mutations."""
import argparse
import collections
import csv
import json
import pathlib
import sqlite3
import sys

GUIDES = ("sampleapp-web-guide", "sampleapp-windows-guide")
SUPPORTED_PRIMARY = {"clicked", "target-disappeared", "value-equals", "value-not-empty"}
SUPPORTED_COMPLETION = {"target-exists", "target-not-exists", "target-enabled", "value-equals"}

def read_table(con, table, columns, where="", params=()):
    if table not in {r[0] for r in con.execute("SELECT name FROM sqlite_master WHERE type='table'")}:
        return []
    existing = {r[1] for r in con.execute(f"PRAGMA table_info({table})")}
    selected = [x for x in columns if x in existing]
    if not selected:
        return []
    return [dict(zip(selected, row)) for row in con.execute(
        f"SELECT {','.join(selected)} FROM {table} {where}", params)]

def classify(s, anchors, captures, conditions):
    issues = []
    kind = (s.get("ValidationKind") or "").lower()
    mode = s.get("AdvanceMode")
    target = bool(s.get("Runtime") and s.get("LocatorStrategy") and s.get("LocatorValue"))
    centered = s.get("BubblePlacement") == "Center"
    if kind and kind not in SUPPORTED_PRIMARY:
        issues.append("primary-validation-requires-review:" + kind)
    if mode not in {"AutomaticOnValidation", "Manual"}:
        issues.append("unknown-advance-mode:" + str(mode))
    if mode == "AutomaticOnValidation" and not kind:
        issues.append("automatic-without-primary-validation")
    if centered and (target or mode != "Manual" or kind or captures or conditions or s.get("ContextKind")):
        issues.append("centered-info-invariant-review")
    if not centered and not target:
        issues.append("target-attached-without-target")
    for c in conditions:
        if (c.get("Kind") or "").lower() not in SUPPORTED_COMPLETION:
            issues.append("completion-requires-review:" + str(c.get("Kind")))
    if s.get("FrameContextJson"):
        try:
            json.loads(s["FrameContextJson"])
        except (TypeError, ValueError):
            issues.append("invalid-frame-context-json")
    for c in conditions:
        if c.get("TargetJson"):
            try:
                json.loads(c["TargetJson"])
            except (TypeError, ValueError):
                issues.append("invalid-completion-target-json")
    if kind in {"value-equals", "value-not-empty"}:
        family = "value-entry-or-selection"
    elif kind == "clicked":
        family = "click"
    elif kind == "target-disappeared":
        family = "wait-for-disappearance"
    elif centered:
        family = "information"
    elif mode == "Manual":
        family = "manual-target-step"
    else:
        family = "unclassified"
    # Family is inferred from validation only; it is NOT proof of the user's actual action.
    return family, issues

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--db", default=r"C:\ProgramData\GuideMe\Data\GuideMe.db")
    ap.add_argument("--out", default="instructor-coverage-audit")
    args = ap.parse_args()
    db = pathlib.Path(args.db)
    if not db.is_file():
        ap.error(f"Database does not exist: {db}")
    uri = db.resolve().as_uri() + "?mode=ro"
    con = sqlite3.connect(uri, uri=True)
    con.row_factory = sqlite3.Row
    try:
        con.execute("PRAGMA query_only = ON")
        guide_rows = read_table(con, "Guides", ["Id", "Key", "Name"])
        guides = {r["Key"]: r for r in guide_rows if r["Key"] in GUIDES}
        if len(guides) != 2:
            missing = set(GUIDES) - set(guides)
            raise RuntimeError(f"Required guides missing: {sorted(missing)}")
        rows = []
        for key in GUIDES:
            steps = read_table(con, "GuideSteps", [
                "Id", "GuideId", "Key", "StepOrder", "AdvanceMode", "Runtime",
                "LocatorStrategy", "LocatorValue", "FrameContextJson", "ContextKind",
                "ContextValue", "BubbleContent", "BubblePlacement", "ValidationKind",
                "ValidationExpectedValue", "ValidationOptionsJson", "IsEnabled",
                "AutomationValue", "ApplicationContextKey"
            ], "WHERE GuideId = ? ORDER BY StepOrder", (guides[key]["Id"],))
            for s in steps:
                sid = s["Id"]
                anchors = read_table(con, "TargetAnchors",
                    ["AnchorOrder", "Relation", "LocatorStrategy", "LocatorValue"],
                    "WHERE GuideStepId = ? ORDER BY AnchorOrder", (sid,))
                captures = read_table(con, "StepCaptures",
                    ["Runtime", "LocatorStrategy", "LocatorValue", "Property", "Pattern"],
                    "WHERE GuideStepId = ?", (sid,))
                conditions = read_table(con, "StepCompletionConditions",
                    ["ConditionOrder", "Kind", "ExpectedValue", "TargetJson"],
                    "WHERE GuideStepId = ? ORDER BY ConditionOrder", (sid,))
                family, issues = classify(s, anchors, captures, conditions)
                rows.append({
                    "guide": key, "step": s["StepOrder"], "step_key": s["Key"],
                    "family_inferred": family, "runtime": s.get("Runtime"),
                    "locator_strategy": s.get("LocatorStrategy"),
                    "locator_value": s.get("LocatorValue"),
                    "frame_context": s.get("FrameContextJson"),
                    "context_kind": s.get("ContextKind"),
                    "context_value": s.get("ContextValue"),
                    "bubble": s.get("BubbleContent"),
                    "bubble_placement": s.get("BubblePlacement"),
                    "validation_kind": s.get("ValidationKind"),
                    "validation_expected": s.get("ValidationExpectedValue"),
                    "validation_options": s.get("ValidationOptionsJson"),
                    "advance_mode": s.get("AdvanceMode"),
                    "enabled": s.get("IsEnabled"),
                    "automation_value": s.get("AutomationValue"),
                    "application_context_key": s.get("ApplicationContextKey"),
                    "anchors": anchors, "captures": captures,
                    "completion_conditions": conditions, "issues": issues
                })
        output = pathlib.Path(args.out)
        output.parent.mkdir(parents=True, exist_ok=True)
        json_path = output.with_suffix(".json")
        csv_path = output.with_suffix(".csv")
        json_path.write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding="utf-8")
        columns = ["guide", "step", "step_key", "family_inferred", "runtime",
                   "locator_strategy", "locator_value", "frame_context",
                   "context_kind", "context_value", "bubble", "bubble_placement",
                   "validation_kind", "validation_expected", "validation_options",
                   "advance_mode", "enabled", "automation_value", "application_context_key",
                   "anchors", "captures", "completion_conditions", "issues"]
        with csv_path.open("w", newline="", encoding="utf-8-sig") as f:
            writer = csv.DictWriter(f, fieldnames=columns)
            writer.writeheader()
            for row in rows:
                writer.writerow({k: json.dumps(v, ensure_ascii=False) if isinstance(v, list) else v for k, v in row.items()})
        print("Read-only audit complete")
        print("Guides:", ", ".join(f"{g}: {sum(r['guide']==g for r in rows)} steps" for g in GUIDES))
        print("Families:", dict(collections.Counter(r["family_inferred"] for r in rows)))
        print("Anchors:", sum(len(r["anchors"]) for r in rows))
        print("Captures:", sum(len(r["captures"]) for r in rows))
        print("Completion conditions:", sum(len(r["completion_conditions"]) for r in rows))
        print("Rows requiring review:", sum(bool(r["issues"]) for r in rows))
        print("JSON:", json_path.resolve())
        print("CSV:", csv_path.resolve())
        print("NOTE: Family inference is heuristic; successful coverage requires runtime and authoring verification.")
        print("NOTE: Export contains guide text and locator values. Review before sharing externally.")
    finally:
        con.close()

if __name__ == "__main__":
    try:
        main()
    except (sqlite3.Error, OSError, RuntimeError) as e:
        print(f"Audit failed: {e}", file=sys.stderr)
        sys.exit(1)
