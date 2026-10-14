"""Read-only capability inventory for the proposed 40-step reference Guide.

Counts *configured* capabilities, not successful runtime executions.
Never modifies SQLite. Reports separate Web and Windows percentages.
"""
import argparse
import json
import sqlite3
from pathlib import Path

DEFAULT_DB = r"C:\ProgramData\GuideMe\Data\GuideMe.db"
SOURCES = (("Web", "sampleapp-web-guide", 20),
           ("Windows", "sampleapp-windows-guide", 20))
# Explicit, source-audited capability inventory. A runtime's denominator
# excludes capabilities that are not applicable or not implemented there.
CATALOG = {
    "Web": {
        "locator": ["css", "text", "label", "role"],
        "anchor": ["Ancestor", "Context", "Descendant", "Sibling", "Nearby", "multiple"],
        "completion": ["target-exists", "target-not-exists", "target-enabled", "value-equals"],
        "capture_timing": ["BeforeAction", "DuringStep", "AfterAction"],
        "presentation": ["Auto", "Top", "Right", "Bottom", "Left", "Center", "targetless"],
        "advance": ["Manual", "Automatic"],
        "frame": ["nested-frame"],
    },
    "Windows": {
        "locator": ["automation-id", "name", "control-type", "name-regex"],
        "anchor": ["Self", "Context", "Ancestor", "Descendant", "Sibling", "Nearby", "ColumnHeader", "multiple"],
        "completion": ["target-exists", "target-not-exists", "target-enabled", "value-equals", "target-replaced"],
        "capture_timing": ["BeforeAction", "DuringStep", "AfterAction"],
        "presentation": ["Auto", "Top", "Right", "Bottom", "Left", "Center", "targetless"],
        "advance": ["Manual", "Automatic"],
    },
}


def read_rows(conn, sql, args=()):
    return [dict(row) for row in conn.execute(sql, args)]


def collect(conn, key, limit):
    guide = read_rows(conn, "SELECT Id FROM Guides WHERE Key=?", (key,))
    if len(guide) != 1:
        raise ValueError(f"Guide missing or duplicated: {key}")
    steps = read_rows(conn,
        "SELECT * FROM GuideSteps WHERE GuideId=? AND StepOrder BETWEEN 1 AND ? ORDER BY StepOrder",
        (guide[0]["Id"], limit))
    if len(steps) != limit:
        raise ValueError(f"Expected {limit} steps for {key}, got {len(steps)}")
    for step in steps:
        sid = step["Id"]
        for table in ("TargetAnchors", "StepCaptures", "StepCompletionConditions"):
            step[table] = read_rows(conn, f"SELECT * FROM {table} WHERE GuideStepId=?", (sid,))
    return steps


def observed(steps):
    found = {k: set() for k in ("locator", "anchor", "completion", "capture_timing",
                                 "presentation", "advance", "frame")}
    for step in steps:
        if step["LocatorStrategy"]:
            found["locator"].add(step["LocatorStrategy"].lower())
        anchors = step["TargetAnchors"]
        if len(anchors) >= 2:
            found["anchor"].add("multiple")
        for anchor in anchors:
            found["anchor"].add(anchor["Relation"])
        for condition in step["StepCompletionConditions"]:
            found["completion"].add(condition["Kind"])
        for capture in step["StepCaptures"]:
            if capture["Timing"]:
                found["capture_timing"].add(capture["Timing"])
        if step["BubblePlacement"]:
            found["presentation"].add(step["BubblePlacement"])
        if not step["LocatorStrategy"]:
            found["presentation"].add("targetless")
        if step["AdvanceMode"]:
            found["advance"].add(step["AdvanceMode"])
        if step["FrameContextJson"] and step["FrameContextJson"] not in ("[]", "null"):
            found["frame"].add("nested-frame")
    return found


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--db", default=DEFAULT_DB)
    parser.add_argument("--output", default="reference-guide-coverage.json")
    args = parser.parse_args()
    path = Path(args.db)
    if not path.is_file():
        parser.error(f"Database not found: {path}")
    with sqlite3.connect(path.resolve().as_uri() + "?mode=ro", uri=True, timeout=5) as conn:
        conn.row_factory = sqlite3.Row
        result = {"Status": "PROPOSED_CONFIG_ONLY_NOT_RUNTIME_TESTED",
                  "Source": "First 20 persisted steps of each canonical Guide",
                  "Runtimes": {}}
        for runtime, key, count in SOURCES:
            seen = observed(collect(conn, key, count))
            groups = {}
            for group, possible in CATALOG[runtime].items():
                configured = sorted(set(possible) & seen[group])
                missing = sorted(set(possible) - seen[group])
                groups[group] = {
                    "configured": configured, "missing": missing,
                    "covered": len(configured), "applicable": len(possible),
                    "percent": round(100 * len(configured) / len(possible), 1)}
            covered = sum(x["covered"] for x in groups.values())
            total = sum(x["applicable"] for x in groups.values())
            result["Runtimes"][runtime] = {
                "StepCount": count, "ConfiguredCoveragePercent": round(100 * covered / total, 1),
                "Configured": covered, "Applicable": total,
                "VerifiedRuntimeCoveragePercent": None,
                "Groups": groups}
            print(f"{runtime}: {covered}/{total} configured capabilities ({100 * covered / total:.1f}%). Runtime verification: pending.")
            for group, data in groups.items():
                print(f"  {group}: {data['covered']}/{data['applicable']}; missing: {', '.join(data['missing']) or '(none)'}")
        print("Note: coverage inventory is an explicit baseline, not an exhaustive feature contract.")
        Path(args.output).write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"Report: {args.output}")


if __name__ == "__main__":
    main()
