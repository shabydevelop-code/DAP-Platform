"""Build a 110-step mixed Web/Windows reference from persisted canonical guides.

The existing six-step reference is replaced only with --apply, after checking its
expected step keys. This does not certify runtime behavior or full feature coverage. Canonical guides and smoke guide are never edited.
Use --preview (default) to inspect the proposed step order and capability counts.
"""
import argparse
import re
import sqlite3
from collections import Counter
from pathlib import Path

DB = Path(r"C:\ProgramData\GuideMe\Data\GuideMe.db")
DEST = "sampleapp-reference-guide"
SOURCES = (("sampleapp-web-guide", "web", 55),
           ("sampleapp-windows-guide", "windows", 55))
EXPECTED_BASELINE = ("web-testcrm-customer-name", "web-testcrm-customer-search-button",
                     "windows-testcrm-windows-customer-name",
                     "windows-testcrm-windows-customer-search-button",
                     "web-testcrm-customer-result", "web-testcrm-site-row")
CHILD_TABLES = ("TargetAnchors", "StepCaptures", "StepCompletionConditions")


def columns(conn, table):
    return [row[1] for row in conn.execute(f"PRAGMA table_info({table})")]


def copy_row(conn, table, row, updates):
    names = [name for name in columns(conn, table) if name != "Id"]
    vals = [updates.get(name, row[name]) for name in names]
    return conn.execute(
        f"INSERT INTO {table} ({','.join(names)}) VALUES ({','.join('?' for _ in names)})",
        vals).lastrowid


def fetch_one(conn, sql, args):
    row = conn.execute(sql, args).fetchone()
    if row is None:
        raise ValueError(f"Missing expected record for {args}")
    return row


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--db", default=str(DB))
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    path = Path(args.db)
    if not path.is_file():
        parser.error(f"Database not found: {path}")
    uri = path.resolve().as_uri() + ("?mode=rw" if args.apply else "?mode=ro")
    with sqlite3.connect(uri, uri=True, timeout=5) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("PRAGMA busy_timeout=5000")
        dest = fetch_one(conn, "SELECT * FROM Guides WHERE Key=?", (DEST,))
        current = conn.execute(
            "SELECT Key FROM GuideSteps WHERE GuideId=? ORDER BY StepOrder",
            (dest["Id"],)).fetchall()
        actual_keys = tuple(r["Key"] for r in current)
        if actual_keys != EXPECTED_BASELINE:
            raise ValueError("Reference Guide is not the expected six-step baseline; refusing replacement")
        source_data = []
        for guide_key, prefix, count in SOURCES:
            guide = fetch_one(conn, "SELECT * FROM Guides WHERE Key=?", (guide_key,))
            steps = conn.execute(
                "SELECT * FROM GuideSteps WHERE GuideId=? AND StepOrder BETWEEN 1 AND ? ORDER BY StepOrder",
                (guide["Id"], count)).fetchall()
            if len(steps) != count or [s["StepOrder"] for s in steps] != list(range(1, count + 1)):
                raise ValueError(f"Missing canonical steps in {guide_key}")
            contexts = conn.execute(
                "SELECT * FROM GuideApplicationContexts WHERE GuideId=?", (guide["Id"],)).fetchall()
            source_data.append((prefix, steps, contexts))
        print("Proposed 110-step reference (CONFIG ONLY; runtime continuity unverified):")
        for prefix, steps, _ in source_data:
            for step in steps:
                print(f"  {prefix.upper()} {step['StepOrder']:02d}: {step['Key']}")
        if not args.apply:
            print("WARNING: This copies canonical steps; it does not add missing capabilities.")
            print("WARNING: Cross-runtime continuity and completion-bubble transitions require testing.")
            print("PREVIEW ONLY; database unchanged. Run with --apply to install.")
            return
        conn.execute("PRAGMA foreign_keys=ON")
        conn.execute("BEGIN IMMEDIATE")
        # Remove dependent records first so the operation works with or without
        # database-level cascading foreign keys.
        for step in conn.execute(
            "SELECT Id FROM GuideSteps WHERE GuideId=?", (dest["Id"],)).fetchall():
            for table in CHILD_TABLES:
                conn.execute(f"DELETE FROM {table} WHERE GuideStepId=?", (step["Id"],))
        conn.execute("DELETE FROM GuideSteps WHERE GuideId=?", (dest["Id"],))
        for context in conn.execute(
            "SELECT Id FROM GuideApplicationContexts WHERE GuideId=?", (dest["Id"],)).fetchall():
            conn.execute("DELETE FROM ApplicationContextMatchers WHERE ApplicationContextId=?", (context["Id"],))
        conn.execute("DELETE FROM GuideApplicationContexts WHERE GuideId=?", (dest["Id"],))
        order = 0
        for prefix, steps, contexts in source_data:
            context_map = {}
            for ctx in contexts:
                new_key = f"{prefix}-{ctx['Key']}"
                new_ctx_id = copy_row(conn, "GuideApplicationContexts", ctx,
                                      {"GuideId": dest["Id"], "Key": new_key})
                for matcher in conn.execute(
                    "SELECT * FROM ApplicationContextMatchers WHERE ApplicationContextId=? ORDER BY MatcherOrder",
                    (ctx["Id"],)).fetchall():
                    copy_row(conn, "ApplicationContextMatchers", matcher,
                             {"ApplicationContextId": new_ctx_id})
                context_map[ctx["Key"]] = new_key
            for step in steps:
                order += 1
                def remap(value):
                    if not isinstance(value, str):
                        return value
                    return re.sub(r"\{\{step:([^}:]+):capture\}\}",
                                  lambda m: "{{step:" + prefix + "-" + m.group(1) + ":capture}}",
                                  value)
                changes = {"GuideId": dest["Id"], "StepOrder": order,
                           "Key": prefix + "-" + step["Key"],
                           "ApplicationContextKey": context_map.get(step["ApplicationContextKey"])}
                # Captured values can appear in primary locators, frame data or
                # related strings. Preserve exact source definitions otherwise.
                for name in columns(conn, "GuideSteps"):
                    if name not in ("Id", "Key", "ApplicationContextKey"):
                        changes.setdefault(name, remap(step[name]))
                new_step_id = copy_row(conn, "GuideSteps", step, changes)
                for table in CHILD_TABLES:
                    children = conn.execute(
                        f"SELECT * FROM {table} WHERE GuideStepId=? ORDER BY rowid",
                        (step["Id"],)).fetchall()
                    for child in children:
                        overrides = {"GuideStepId": new_step_id}
                        for field in columns(conn, table):
                            if field not in ("Id", "GuideStepId"):
                                overrides[field] = remap(child[field])
                        copy_row(conn, table, child, overrides)
        count = conn.execute(
            "SELECT COUNT(*) FROM GuideSteps WHERE GuideId=?", (dest["Id"],)).fetchone()[0]
        if count != 110:
            raise RuntimeError(f"Expected 110 steps, got {count}")
        conn.commit()
        print(f"Installed {DEST}: {count} steps; canonical guides unchanged")
        print("UNVERIFIED: runtime flow and missing feature coverage must still be implemented.")


if __name__ == "__main__":
    main()
