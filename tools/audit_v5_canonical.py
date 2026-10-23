#!/usr/bin/env python3
"""Read-only structural audit of three canonical V5 guides. No migrations."""
import argparse
import json
import sqlite3
import sys
from collections import Counter, defaultdict
from pathlib import Path

KEYS = ("sampleapp-web-guide", "sampleapp-windows-guide", "sampleapp-mixed-smoke-guide")
TABLES = ("Guides", "ApplicationContexts", "Steps", "Elements",
          "ElementAnchors", "StepTargets", "StepRules")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--db", type=Path, default=Path(r"C:\ProgramData\GuideMe\Data\GuideMe-V5.db"))
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if not args.db.is_file():
        parser.error(f"V5 database missing: {args.db}")
    if args.output and args.output.resolve() == args.db.resolve():
        parser.error("Report path must not overwrite the database")
    connection = sqlite3.connect(f"file:{args.db.resolve().as_posix()}?mode=ro", uri=True, timeout=5)
    connection.row_factory = sqlite3.Row
    connection.execute("PRAGMA query_only=ON")
    try:
        def rows(sql, params=()):
            return [dict(r) for r in connection.execute(sql, params)]

        report = {"database": str(args.db), "read_only": True, "guides": [],
                  "foreign_key_violations": [], "total_issues": 0,
                  "limitations": [
                      "Structural audit only: runtime behavior and semantic equivalence are not proven.",
                      "Compilation must be checked separately using the production V5 validator.",
                      "No legacy database is required or consulted."]}
        present = {r["name"] for r in rows("SELECT name FROM sqlite_master WHERE type='table'")}
        missing = set(TABLES) - present
        if missing:
            raise ValueError("Missing V5 tables: " + ", ".join(sorted(missing)))
        report["foreign_key_violations"] = rows("PRAGMA foreign_key_check")
        for key in KEYS:
            issues = []
            def issue(code, message, step=None):
                issues.append({"code": code, "step": step, "detail": message})
            matches = rows("SELECT GuideId,GuideName FROM Guides WHERE GuideKey=?", (key,))
            if len(matches) != 1:
                issue("guide_missing", f"Expected one guide, found {len(matches)}")
                report["guides"].append({"key": key, "issues": issues})
                continue
            gid = matches[0]["GuideId"]
            def table(name):
                return rows(f"SELECT * FROM {name} WHERE GuideId=?", (gid,))
            contexts = {r["ContextId"]: r for r in table("ApplicationContexts")}
            steps = sorted(table("Steps"), key=lambda x: x["StepOrder"])
            elements = {r["ElementId"]: r for r in table("Elements")}
            anchors = table("ElementAnchors")
            targets = table("StepTargets")
            rules = table("StepRules")
            targets_by_step = defaultdict(list)
            rules_by_step = defaultdict(list)
            for t in targets:
                targets_by_step[t["StepId"]].append(t)
            for rule in rules:
                rules_by_step[rule["StepId"]].append(rule)
            for c in contexts.values():
                try:
                    matchers = json.loads(c["MatchersJson"])
                    if not isinstance(matchers, list):
                        raise ValueError("not an array")
                except (ValueError, TypeError):
                    issue("invalid_context_matchers", c["ContextId"])
            for e in elements.values():
                try:
                    locators = json.loads(e["LocatorsJson"])
                    if not isinstance(locators, list) or not locators:
                        raise ValueError("no locators")
                    for locator in locators:
                        if not isinstance(locator, dict) or not locator.get("Strategy") or not locator.get("Value"):
                            raise ValueError("invalid locator")
                except (ValueError, TypeError):
                    issue("invalid_element_locators", e["ElementId"])
                if e["FrameContextJson"]:
                    try:
                        frame = json.loads(e["FrameContextJson"])
                        path = frame.get("Path") if isinstance(frame, dict) else frame
                        if not isinstance(path, list):
                            raise ValueError("invalid frame path")
                    except (ValueError, TypeError):
                        issue("invalid_frame_context", e["ElementId"])
            orders = [s["StepOrder"] for s in steps]
            if len(orders) != len(set(orders)):
                issue("duplicate_order", "Duplicate StepOrder")
            if orders and orders != list(range(orders[0], orders[0] + len(orders))):
                issue("order_gaps", f"StepOrder sequence has gaps: {orders}")
            for s in steps:
                sid, order = s["StepId"], s["StepOrder"]
                ctx = contexts.get(s["ApplicationContextId"])
                if ctx is None:
                    issue("missing_application_context", str(s["ApplicationContextId"]), order)
                if not s["BubbleContent"]:
                    issue("empty_bubble", sid, order)
                primary = [t for t in targets_by_step[sid] if t["Purpose"] == "Primary"]
                validations = [r for r in rules_by_step[sid] if r["RuleType"] == "PrimaryValidation"]
                if len(primary) > 1:
                    issue("multiple_primary_targets", sid, order)
                if len(validations) > 1:
                    issue("multiple_primary_rules", sid, order)
                if s["IsEnabled"] and primary and len(validations) != 1:
                    issue("missing_primary_validation", sid, order)
                if s["IsEnabled"] and validations and not primary:
                    issue("validation_without_primary_target", sid, order)
                for t in targets_by_step[sid]:
                    e = elements.get(t["ElementId"])
                    if e is None:
                        issue("missing_element", t["ElementId"], order)
                    elif ctx and e["Runtime"] and e["Runtime"] != ctx["Runtime"]:
                        issue("target_runtime_mismatch", t["ElementId"], order)
                usages = {t["UsageId"] for t in targets_by_step[sid]}
                for rule in rules_by_step[sid]:
                    if rule["TargetUsageId"] is not None and rule["TargetUsageId"] not in usages:
                        issue("unresolved_rule_target", rule["RuleId"], order)
                    if rule["RuleType"] == "Capture" and not rule["Property"]:
                        issue("capture_without_property", rule["RuleId"], order)
                    if rule["RuleType"] != "Capture" and not rule["Kind"]:
                        issue("validation_without_kind", rule["RuleId"], order)
                    if rule["OptionsJson"]:
                        try:
                            json.loads(rule["OptionsJson"])
                        except (ValueError, TypeError):
                            issue("invalid_rule_options", rule["RuleId"], order)
            for anchor in anchors:
                if anchor["ElementId"] not in elements or anchor["AnchorElementId"] not in elements:
                    issue("unresolved_anchor", str(anchor))
            for t in targets:
                if t["StepId"] not in {s["StepId"] for s in steps}:
                    issue("orphan_target", t["StepId"])
            for rule in rules:
                if rule["StepId"] not in {s["StepId"] for s in steps}:
                    issue("orphan_rule", rule["StepId"])
            report["guides"].append({
                "key": key, "guide_id": gid, "guide_name": matches[0]["GuideName"],
                "counts": {"steps": len(steps), "contexts": len(contexts),
                           "elements": len(elements), "anchors": len(anchors),
                           "targets": len(targets), "rules": len(rules)},
                "runtime_steps": dict(Counter(
                    contexts[s["ApplicationContextId"]]["Runtime"]
                    for s in steps if s["ApplicationContextId"] in contexts)),
                "issue_counts": dict(Counter(x["code"] for x in issues)),
                "issues": issues})
        report["total_issues"] = sum(len(g["issues"]) for g in report["guides"]) + len(report["foreign_key_violations"])
        output = json.dumps(report, ensure_ascii=False, indent=2)
        if args.output:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(output, encoding="utf-8")
            print(f"Report: {args.output}")
        else:
            print(output)
        print(f"V5 audit: {report['total_issues']} issue(s), {len(report['guides'])} guide(s)")
        return 0 if report["total_issues"] == 0 else 2
    finally:
        connection.close()


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (sqlite3.Error, OSError, ValueError) as ex:
        print(f"AUDIT ERROR: {ex}", file=sys.stderr)
        sys.exit(1)
