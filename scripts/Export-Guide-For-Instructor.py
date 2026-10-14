"""Read-only export of a persisted Guide and its related authoring data."""
import argparse
import json
import sqlite3
from pathlib import Path

DEFAULT_DB = r"C:\ProgramData\GuideMe\Data\GuideMe.db"
DEFAULT_GUIDE = "sampleapp-mixed-smoke-guide"

def rows(conn, sql, params=()):
    return [dict(row) for row in conn.execute(sql, params)]

def exists(conn, table):
    return conn.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name=?", (table,)).fetchone() is not None

def export(conn, guide_key):
    guide = rows(conn, "SELECT * FROM Guides WHERE Key=?", (guide_key,))
    if len(guide) != 1:
        raise ValueError(f"Expected one Guide with key {guide_key!r}; found {len(guide)}")
    gid = guide[0]["Id"]
    steps = rows(conn, "SELECT * FROM GuideSteps WHERE GuideId=? ORDER BY StepOrder", (gid,))
    result = {"Guide": guide[0], "ApplicationContexts": [], "Steps": []}
    if exists(conn, "GuideApplicationContexts"):
        contexts = rows(conn, "SELECT * FROM GuideApplicationContexts WHERE GuideId=? ORDER BY Id", (gid,))
        for ctx in contexts:
            if exists(conn, "ApplicationContextMatchers"):
                ctx["Matchers"] = rows(conn, "SELECT * FROM ApplicationContextMatchers WHERE ApplicationContextId=? ORDER BY MatcherOrder", (ctx["Id"],))
            result["ApplicationContexts"].append(ctx)
    for step in steps:
        sid = step["Id"]
        for table in ("TargetAnchors", "StepCaptures", "StepCompletionConditions"):
            if exists(conn, table):
                step[table] = rows(conn, f"SELECT * FROM {table} WHERE GuideStepId=? ORDER BY rowid", (sid,))
        result["Steps"].append(step)
    return result

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--db", default=DEFAULT_DB)
    parser.add_argument("--guide", default=DEFAULT_GUIDE)
    parser.add_argument("--output", default="mixed-guide-export.json")
    args = parser.parse_args()
    uri = Path(args.db).resolve().as_uri() + "?mode=ro"
    with sqlite3.connect(uri, uri=True) as conn:
        conn.row_factory = sqlite3.Row
        result = export(conn, args.guide)
    Path(args.output).write_text(json.dumps(result, ensure_ascii=False, indent=2, default=str), encoding="utf-8")
    print(f"Exported {len(result['Steps'])} steps and {len(result['ApplicationContexts'])} contexts to {args.output}")

if __name__ == "__main__":
    main()
