"""Clone the installed 55-step Web Guide without modifying its source.

The clone is deliberately not converted to state-only: transition semantics must
be verified individually before replacing click validation. Run with --apply.
"""
import argparse
import sqlite3
from pathlib import Path

DEFAULT_DB = r"C:\ProgramData\GuideMe\Data\GuideMe.db"
SOURCE_KEY = "sampleapp-web-guide"
CLONE_KEY = "sampleapp-web-state-guide"

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--db", default=DEFAULT_DB)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    db = Path(args.db)
    if not db.is_file():
        parser.error(f"Database missing: {db}")
    with sqlite3.connect(db.resolve().as_uri() + ("?mode=rw" if args.apply else "?mode=ro"),
                         uri=True, timeout=5) as conn:
        conn.row_factory = sqlite3.Row
        conn.execute("PRAGMA foreign_keys=ON")
        src = conn.execute("SELECT Id FROM Guides WHERE Key=?", (SOURCE_KEY,)).fetchone()
        if not src:
            parser.error(f"Source Guide missing: {SOURCE_KEY}")
        source_id = src["Id"]
        steps = conn.execute("SELECT Id FROM GuideSteps WHERE GuideId=? ORDER BY StepOrder",
                             (source_id,)).fetchall()
        if len(steps) != 55:
            parser.error(f"Expected 55 steps, got {len(steps)}")
        if conn.execute("SELECT 1 FROM Guides WHERE Key=?", (CLONE_KEY,)).fetchone():
            parser.error(f"Clone already exists: {CLONE_KEY}; refusing overwrite")
        print(f"Source: {SOURCE_KEY}; steps: 55; destination: {CLONE_KEY}")
        if not args.apply:
            print("PREVIEW ONLY. Use --apply to create an isolated exact clone.")
            return
        conn.execute("BEGIN IMMEDIATE")
        def columns(table):
            return [r["name"] for r in conn.execute(f"PRAGMA table_info({table})")]
        def duplicate(table, old_id, overrides):
            cols = [c for c in columns(table) if c != "Id"]
            values = {c: conn.execute(f'SELECT "{c}" FROM "{table}" WHERE Id=?',
                                      (old_id,)).fetchone()[0] for c in cols}
            values.update(overrides)
            quoted = ", ".join('"' + c + '"' for c in cols)
            marks = ", ".join("?" for _ in cols)
            return conn.execute(f'INSERT INTO "{table}" ({quoted}) VALUES ({marks})',
                                tuple(values[c] for c in cols)).lastrowid
        gid = duplicate("Guides", source_id, {"Key": CLONE_KEY, "Name": "SampleApp Web State Guide (Unconverted)"})
        # Context keys are scoped to each guide; preserve matchers and ordering.
        contexts = conn.execute("SELECT Id FROM GuideApplicationContexts WHERE GuideId=?",
                                (source_id,)).fetchall()
        for context in contexts:
            cid = duplicate("GuideApplicationContexts", context["Id"], {"GuideId": gid})
            for matcher in conn.execute("SELECT Id FROM ApplicationContextMatchers WHERE ApplicationContextId=?",
                                        (context["Id"],)).fetchall():
                duplicate("ApplicationContextMatchers", matcher["Id"], {"ApplicationContextId": cid})
        for original in steps:
            sid = duplicate("GuideSteps", original["Id"], {"GuideId": gid})
            for table in ("TargetAnchors", "StepCompletionConditions"):
                for row in conn.execute(f"SELECT Id FROM {table} WHERE GuideStepId=?",
                                        (original["Id"],)).fetchall():
                    duplicate(table, row["Id"], {"GuideStepId": sid})
        copied = conn.execute("SELECT COUNT(*) FROM GuideSteps WHERE GuideId=?", (gid,)).fetchone()[0]
        if copied != 55:
            raise RuntimeError("Clone incomplete; rolling back")
        conn.commit()
        print(f"CREATED: {CLONE_KEY}; Id={gid}; steps={copied}")
        print("NOT STATE-ONLY YET. Existing click validation remains intact; do not use for acceptance testing.")

if __name__ == "__main__":
    main()
