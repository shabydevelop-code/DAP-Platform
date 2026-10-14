"""Rename legacy TestCRM step keys in the isolated SampleApp state Guide.

Original Guide is never modified. Preview by default; --apply writes in one
transaction. Does not change step IDs, locators, validation or guide references.
"""
import argparse
import sqlite3
from pathlib import Path

DB = r"C:\ProgramData\GuideMe\Data\GuideMe.db"
ORIGINAL = "sampleapp-web-guide"
CLONE = "sampleapp-web-state-guide"

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--db", default=DB)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    path = Path(args.db)
    if not path.is_file():
        parser.error(f"Database not found: {path}")
    uri = path.resolve().as_uri() + ("?mode=rw" if args.apply else "?mode=ro")
    with sqlite3.connect(uri, uri=True, timeout=5) as conn:
        conn.row_factory = sqlite3.Row
        gids = dict(conn.execute("SELECT Key, Id FROM Guides WHERE Key IN (?, ?)", (ORIGINAL, CLONE)))
        if len(gids) != 2:
            parser.error("Both original and cloned Guides must exist")
        original = conn.execute("SELECT Id, StepOrder, Key FROM GuideSteps WHERE GuideId=? ORDER BY StepOrder", (gids[ORIGINAL],)).fetchall()
        cloned = conn.execute("SELECT Id, StepOrder, Key FROM GuideSteps WHERE GuideId=? ORDER BY StepOrder", (gids[CLONE],)).fetchall()
        if len(original) != 55 or len(cloned) != 55:
            parser.error("Expected 55 steps in both Guides")
        updates = []
        for source, dest in zip(original, cloned):
            if source["StepOrder"] != dest["StepOrder"]:
                parser.error("Step order mismatch")
            expected = source["Key"].replace("testcrm-", "sampleapp-", 1) if source["Key"].startswith("testcrm-") else source["Key"]
            if dest["Key"] not in (source["Key"], expected):
                parser.error(f"Unexpected key at step {dest['StepOrder']}: {dest['Key']}")
            if dest["Key"] != expected:
                updates.append((expected, dest["Id"]))
                print(f"{dest['StepOrder']:2}: {dest['Key']} -> {expected}")
        if len({s["Key"].replace("testcrm-", "sampleapp-", 1) for s in original}) != 55:
            parser.error("Renamed keys would collide")
        print(f"Renames: {len(updates)}; original Guide unchanged")
        if not args.apply:
            print("PREVIEW ONLY. Run --apply after reviewing references to step keys.")
            return
        # Step keys may be referenced outside GuideSteps. Fail closed rather than
        # silently breaking references in unreviewed database columns.
        for (table,) in conn.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"):
            for column in conn.execute(f'PRAGMA table_info("{table}")'):
                name = column["name"]
                if table == "GuideSteps" and name == "Key":
                    continue
                if "key" not in name.lower() and "step" not in name.lower():
                    continue
                try:
                    for source in original:
                        hit = conn.execute(f'SELECT 1 FROM "{table}" WHERE CAST("{name}" AS TEXT)=? LIMIT 1',
                                           (source["Key"],)).fetchone()
                        if hit:
                            parser.error(f"Possible external key reference in {table}.{name}; no changes made")
                except sqlite3.OperationalError:
                    parser.error(f"Could not inspect {table}.{name}; no changes made")
        conn.execute("BEGIN IMMEDIATE")
        for key, step_id in updates:
            conn.execute("UPDATE GuideSteps SET Key=? WHERE Id=?", (key, step_id))
        conn.commit()
        print(f"UPDATED {len(updates)} cloned step keys. Original untouched.")

if __name__ == "__main__":
    main()
