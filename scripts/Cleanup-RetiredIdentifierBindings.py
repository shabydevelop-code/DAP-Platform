"""Remove retired SharedContainer bindings from GuideMe V5 SQLite.

Creates a timestamped database backup. Affected steps are disabled until
their primary target and completion conditions are reauthored. No guesswork.
"""
import argparse
import datetime
import pathlib
import sqlite3

parser = argparse.ArgumentParser()
parser.add_argument("--db", default=r"C:\ProgramData\GuideMe\Data\GuideMe-V5.db")
parser.add_argument("--apply", action="store_true")
args = parser.parse_args()
path = pathlib.Path(args.db)
if not path.is_file():
    raise SystemExit(f"Database not found: {path}")
with sqlite3.connect(f"file:{path}?mode=ro", uri=True) as source:
    rows = source.execute("""
        SELECT DISTINCT a.GuideId, s.StepId, s.StepOrder
        FROM ElementAnchors a
        JOIN StepTargets t ON t.GuideId=a.GuideId AND t.ElementId=a.ElementId
            AND t.Purpose='Primary'
        JOIN Steps s ON s.GuideId=t.GuideId AND s.StepId=t.StepId
        WHERE a.Relation='SharedContainer'
        ORDER BY a.GuideId, s.StepOrder
    """).fetchall()
    count = source.execute(
        "SELECT COUNT(*) FROM ElementAnchors WHERE Relation='SharedContainer'"
    ).fetchone()[0]
    print(f"Retired bindings: {count}; affected steps: {len(rows)}")
    for guide, step, order in rows:
        print(f"Guide {guide}, step {order} ({step})")
    if not args.apply or not count:
        print("No changes. Use --apply to back up and clean.")
    else:
        backup = path.with_name(path.stem + "-before-identifier-cleanup-" +
            datetime.datetime.now().strftime("%Y%m%d-%H%M%S") + path.suffix)
        with sqlite3.connect(backup) as dest:
            source.backup(dest)
        print(f"Backup: {backup}")
if args.apply and count:
    with sqlite3.connect(path) as db:
        db.execute("PRAGMA busy_timeout=3000")
        db.execute("BEGIN IMMEDIATE")
        try:
            db.execute("""
                UPDATE Steps SET IsEnabled=0
                WHERE EXISTS (
                    SELECT 1 FROM StepTargets t
                    JOIN ElementAnchors a ON a.GuideId=t.GuideId
                        AND a.ElementId=t.ElementId
                    WHERE t.GuideId=Steps.GuideId AND t.StepId=Steps.StepId
                        AND t.Purpose='Primary' AND a.Relation='SharedContainer'
                )
            """)
            db.execute("DELETE FROM ElementAnchors WHERE Relation='SharedContainer'")
            db.execute("""
                DELETE FROM Elements
                WHERE NOT EXISTS (
                    SELECT 1 FROM StepTargets t
                    WHERE t.GuideId=Elements.GuideId AND t.ElementId=Elements.ElementId
                )
                AND NOT EXISTS (
                    SELECT 1 FROM ElementAnchors a
                    WHERE a.GuideId=Elements.GuideId
                    AND (a.ElementId=Elements.ElementId
                         OR a.AnchorElementId=Elements.ElementId)
                )
            """)
            db.commit()
        except Exception:
            db.rollback()
            raise
    print("Bindings removed; affected steps disabled pending reauthoring.")
