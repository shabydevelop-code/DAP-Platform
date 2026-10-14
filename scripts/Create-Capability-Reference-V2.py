"""Create a NEW reference guide from explicit SampleApp UI targets, never copied Guide steps.

This is the initial executable authoring slice, NOT the finished coverage guide.
Preview by default. --apply creates a new guide only, never overwrites.
"""
import argparse
import json
import sqlite3
from pathlib import Path

DEFAULT_DB = r"C:\ProgramData\GuideMe\Data\GuideMe.db"
GUIDE = "sampleapp-capability-reference-v2"

# Independently authored from demos/Web/App/wwwroot/app.js and
# demos/Windows/App/MainWindow.xaml.cs. No canonical Guide is read.
STEPS = [
    dict(key="reference-web-customer-name", runtime="Web", strategy="label",
         value="שם לקוח", frame=[{"Strategy":"css","Value":"#content-frame"}],
         anchors=[("Ancestor","css","#customer-search"),("Ancestor","css","label.field")],
         placement="Top", advance="Manual",
         bubble="הזן שם לקוח לחיפוש. זהו שדה בתוך טופס החיפוש."),
    dict(key="reference-web-search", runtime="Web", strategy="role",
         value="button", frame=[{"Strategy":"css","Value":"#content-frame"}],
         anchors=[("Ancestor","css","#customer-search"),("Context","css","button.primary")],
         placement="Right", advance="Manual",
         bubble="הפעל חיפוש באמצעות כפתור החיפוש בטופס."),
    dict(key="reference-windows-customer-name", runtime="Windows",
         strategy="automation-id", value="CustomerNameSearch", frame=None,
         anchors=[("Self","automation-id","CustomerNameSearch")],
         placement="Left", advance="Manual",
         bubble="עבור ליישום החלונות והזן שם לקוח."),
    dict(key="reference-windows-search", runtime="Windows",
         strategy="automation-id", value="SearchCustomersButton", frame=None,
         anchors=[], placement="Bottom", advance="Manual",
         bubble="הפעל חיפוש לקוח ביישום החלונות."),
]
# Do not mark this slice as complete: cross-runtime app matching and runtime
# progression are not yet verified and the complete capability set is pending.


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--db", default=DEFAULT_DB)
    p.add_argument("--apply", action="store_true")
    args = p.parse_args()
    if args.apply:
        p.error("Installation disabled: this four-step draft is incomplete and lacks validated application contexts.")
    db = Path(args.db)
    if not db.is_file():
        p.error(f"Missing database: {db}")
    uri = db.resolve().as_uri() + ("?mode=rw" if args.apply else "?mode=ro")
    with sqlite3.connect(uri, uri=True, timeout=5) as conn:
        conn.row_factory = sqlite3.Row
        if conn.execute("SELECT 1 FROM Guides WHERE Key=?", (GUIDE,)).fetchone():
            raise SystemExit("Guide already exists; no changes made.")
        for table in ("Guides", "GuideSteps", "TargetAnchors"):
            if not conn.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name=?", (table,)).fetchone():
                raise RuntimeError(f"Required table missing: {table}")
        for i, step in enumerate(STEPS, 1):
            print(f"{i}: {step['runtime']} / {step['key']} / {step['strategy']} / {step['value']}")
        if not args.apply:
            print("PREVIEW ONLY. New, independently authored initial slice. NOT full coverage.")
            return
        conn.execute("PRAGMA foreign_keys=ON")
        conn.execute("BEGIN IMMEDIATE")
        guide_id = conn.execute("INSERT INTO Guides(Key,Name) VALUES(?,?)",
                                (GUIDE,"SampleApp Capability Reference V2 - Work in Progress")).lastrowid
        for i, s in enumerate(STEPS, 1):
            step_id = conn.execute(
                """INSERT INTO GuideSteps
                (GuideId,Key,StepOrder,AdvanceMode,Runtime,LocatorStrategy,LocatorValue,
                 FrameContextJson,BubbleContent,BubblePlacement,IsEnabled)
                VALUES(?,?,?,?,?,?,?,?,?,?,?)""",
                (guide_id,s["key"],i,s["advance"],s["runtime"],s["strategy"],s["value"],
                 json.dumps(s["frame"]) if s["frame"] is not None else None,
                 s["bubble"],s["placement"],1)).lastrowid
            for j, (relation,strategy,value) in enumerate(s["anchors"]):
                conn.execute(
                    """INSERT INTO TargetAnchors
                    (GuideStepId,AnchorOrder,Relation,LocatorStrategy,LocatorValue)
                    VALUES(?,?,?,?,?)""",
                    (step_id,j,relation,strategy,value))
        conn.commit()
        print(f"Created {GUIDE}: {len(STEPS)} NEW steps; not runtime-verified.")


if __name__ == "__main__":
    main()
