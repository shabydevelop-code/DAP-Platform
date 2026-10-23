#!/usr/bin/env python3
"""Read-only structural/semantic comparison of canonical legacy and V5 guides.

Does not migrate or modify either database. Produces a JSON report; discrepancies
are evidence for review, not permission to alter production guides.
"""
import argparse
import json
import sqlite3
import sys
from collections import Counter, defaultdict
from pathlib import Path

KEYS = ("sampleapp-web-guide", "sampleapp-windows-guide",
        "sampleapp-mixed-smoke-guide")


def connect(path):
    if not path.is_file():
        raise FileNotFoundError(f"Database missing: {path}")
    db = sqlite3.connect(f"file:{path.resolve().as_posix()}?mode=ro", uri=True, timeout=5)
    db.row_factory = sqlite3.Row
    db.execute("PRAGMA query_only=ON")
    return db


def read(db, sql, args=()):
    return [dict(x) for x in db.execute(sql, args)]


def normalized_json(value):
    if not value:
        return None
    try:
        return json.loads(value)
    except (ValueError, TypeError):
        return {"invalid_json": str(value)}


def audit(old, new, key):
    problems = []
    def flag(code, detail, order=None):
        problems.append({"code": code, "step": order, "detail": detail})

    source = read(old, "SELECT * FROM Guides WHERE Key=?", (key,))
    target = read(new, "SELECT * FROM Guides WHERE GuideKey=?", (key,))
    if len(source) != 1 or len(target) != 1:
        flag("missing_guide", f"legacy={len(source)}, v5={len(target)}")
        return {"key": key, "issues": problems, "legacy_steps": None, "v5_steps": None}

    lid, vid = source[0]["Id"], target[0]["GuideId"]
    legacy = read(old, "SELECT * FROM GuideSteps WHERE GuideId=? ORDER BY StepOrder", (lid,))
    steps = read(new, "SELECT * FROM Steps WHERE GuideId=? ORDER BY StepOrder", (vid,))
    targets = read(new, "SELECT * FROM StepTargets WHERE GuideId=?", (vid,))
    elements = {r["ElementId"]: r for r in read(new, "SELECT * FROM Elements WHERE GuideId=?", (vid,))}
    rules = read(new, "SELECT * FROM StepRules WHERE GuideId=?", (vid,))
    contexts = {r["ContextId"]: r for r in read(new, "SELECT * FROM ApplicationContexts WHERE GuideId=?", (vid,))}
    anchors = read(old, """SELECT s.StepOrder,a.* FROM TargetAnchors a
                           JOIN GuideSteps s ON s.Id=a.GuideStepId
                           WHERE s.GuideId=? ORDER BY s.StepOrder,a.AnchorOrder""", (lid,))
    old_by_order = defaultdict(list)
    v5_by_order = defaultdict(list)
    for row in legacy:
        old_by_order[row["StepOrder"]].append(row)
    for row in steps:
        v5_by_order[row["StepOrder"]].append(row)
    if len(legacy) != len(steps):
        flag("step_count", f"legacy={len(legacy)}, v5={len(steps)}")
    for order in sorted(set(old_by_order) | set(v5_by_order)):
        a, b = old_by_order[order], v5_by_order[order]
        if len(a) != 1 or len(b) != 1:
            flag("step_order", f"legacy={len(a)}, v5={len(b)}", order)
            continue
        a, b = a[0], b[0]
        for old_col, new_col in (("BubbleContent", "BubbleContent"),
                                 ("BubblePlacement", "BubblePlacement"),
                                 ("ContextKind", "ContextKind"),
                                 ("ContextValue", "ContextValue")):
            if a[old_col] != b[new_col]:
                flag("step_field", f"{old_col}: legacy={a[old_col]!r}, v5={b[new_col]!r}", order)
        linked = [t for t in targets if t["StepId"] == b["StepId"] and t["Purpose"] == "Primary"]
        primary = [r for r in rules if r["StepId"] == b["StepId"] and r["RuleType"] == "PrimaryValidation"]
        if a["LocatorValue"] is not None:
            if len(linked) != 1:
                flag("primary_target_count", f"expected 1, found {len(linked)}", order)
            else:
                element = elements.get(linked[0]["ElementId"])
                locators = normalized_json(element["LocatorsJson"]) if element else None
                if not isinstance(locators, list) or not any(
                    x.get("Strategy") == a["LocatorStrategy"] and
                    x.get("Value") == a["LocatorValue"] for x in locators if isinstance(x, dict)
                ):
                    flag("locator_mismatch", f"legacy={a['LocatorStrategy']}:{a['LocatorValue']}, v5={locators}", order)
                if element and normalized_json(a["FrameContextJson"]) != normalized_json(element["FrameContextJson"]):
                    flag("frame_mismatch", "Frame context differs", order)
        elif linked:
            flag("unexpected_target", f"legacy no locator, v5 primary targets={len(linked)}", order)
        if a["ValidationKind"] is not None:
            if len(primary) != 1:
                flag("validation_count", f"expected 1, found {len(primary)}", order)
            else:
                r = primary[0]
                if r["Kind"] != a["ValidationKind"]:
                    flag("validation_kind", f"legacy={a['ValidationKind']}, v5={r['Kind']}", order)
                if r["ExpectedValue"] != a["ValidationExpectedValue"]:
                    flag("validation_expected", "Expected validation value differs", order)
                if normalized_json(r["OptionsJson"]) != normalized_json(a["ValidationOptionsJson"]):
                    flag("validation_options", "Validation options differ", order)
        elif primary:
            flag("unexpected_validation", f"legacy no validation, v5 rules={len(primary)}", order)
        if a["Runtime"] and b["ApplicationContextId"] and b["ApplicationContextId"] in contexts:
            runtime = contexts[b["ApplicationContextId"]]["Runtime"]
            if runtime != a["Runtime"]:
                flag("runtime_mismatch", f"legacy={a['Runtime']}, v5={runtime}", order)
        if a["AdvanceMode"] not in ("Manual", "Auto", "Hybrid"):
            flag("advance_mode_review", f"legacy advance mode={a['AdvanceMode']!r}; compact V5 has no equivalent direct column", order)
    if anchors:
        # An anchor is an explicit part of target identity. Check for loss, without
        # assuming ElementId or StepId is preserved across generations.
        for row in anchors:
            match = v5_by_order.get(row["StepOrder"], [])
            if len(match) != 1:
                continue
            linked = [t for t in targets if t["StepId"] == match[0]["StepId"] and t["Purpose"] == "Primary"]
            related = read(new, "SELECT * FROM ElementAnchors WHERE GuideId=? AND ElementId=?",
                           (vid, linked[0]["ElementId"])) if len(linked) == 1 else []
            if not any(x["AnchorOrder"] == row["AnchorOrder"] and x["Relation"] == row["Relation"] for x in related):
                flag("anchor_missing", f"anchor order={row['AnchorOrder']}, relation={row['Relation']}", row["StepOrder"])
    return {"key": key, "legacy_id": lid, "v5_id": vid,
            "legacy_steps": len(legacy), "v5_steps": len(steps),
            "issue_counts": dict(Counter(p["code"] for p in problems)),
            "issues": problems}


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--legacy", type=Path, default=Path(r"C:\ProgramData\GuideMe\Data\GuideMe.db"))
    ap.add_argument("--v5", type=Path, default=Path(r"C:\ProgramData\GuideMe\Data\GuideMe-V5.db"))
    ap.add_argument("--output", type=Path, help="Optional report JSON path (never a DB)")
    args = ap.parse_args()
    if args.output and args.output.resolve() in (args.legacy.resolve(), args.v5.resolve()):
        ap.error("Refusing to write report over a database")
    with connect(args.legacy) as old, connect(args.v5) as new:
        for db, table in ((old, "GuideSteps"), (new, "StepRules")):
            db.execute(f"SELECT 1 FROM {table} LIMIT 1")
        reports = [audit(old, new, key) for key in KEYS]
    payload = {"read_only": True, "legacy": str(args.legacy), "v5": str(args.v5),
               "guides": reports, "total_issues": sum(len(r["issues"]) for r in reports),
               "limitations": [
                   "Matches steps by order, not stable cross-schema identity",
                   "Checks primary targets/validation and anchor presence; does not prove runtime behavior",
                   "AdvanceMode and additional completion/capture semantics require separate review",
                   "No migration or automatic remediation is performed"]}
    result = json.dumps(payload, ensure_ascii=False, indent=2)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(result, encoding="utf-8")
        print(f"Report: {args.output}")
    else:
        print(result)
    return 0 if payload["total_issues"] == 0 else 2


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, sqlite3.Error, ValueError) as error:
        print(f"AUDIT ERROR: {error}", file=sys.stderr)
        sys.exit(1)
