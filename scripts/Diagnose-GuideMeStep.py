"""Read-only diagnosis of V5 step targets and variable captures."""
import argparse
import json
import sqlite3

parser = argparse.ArgumentParser()
parser.add_argument("--db", default=r"C:\ProgramData\GuideMe\Data\GuideMe-V5.db")
parser.add_argument("--guide", type=int, default=None)
parser.add_argument("--step", default="testcrm-open-created-case")
args = parser.parse_args()

with sqlite3.connect("file:" + args.db.replace("\\", "/") + "?mode=ro", uri=True) as db:
    db.row_factory = sqlite3.Row
    def rows(sql, params=()):
        try:
            return [dict(row) for row in db.execute(sql, params)]
        except sqlite3.OperationalError as exc:
            return [{"error": str(exc)}]

    guides = rows("SELECT * FROM Guides ORDER BY GuideId")
    print("GUIDES:", json.dumps(guides, ensure_ascii=False))
    steps = rows("SELECT GuideId,StepId,ApplicationContextId FROM Steps WHERE StepId=?"
                 + (" AND GuideId=?" if args.guide is not None else ""),
                 (args.step, args.guide) if args.guide is not None else (args.step,))
    print("MATCHED STEPS:", json.dumps(steps, ensure_ascii=False))
    for step in steps:
        guide, sid = step["GuideId"], step["StepId"]
        print("\nGUIDE", guide, "STEP", sid)
        for label, sql in [
            ("PRIMARY", """SELECT t.Purpose,e.ElementId,e.Runtime,e.LocatorsJson,e.FrameContextJson
                FROM StepTargets t JOIN Elements e ON e.GuideId=t.GuideId AND e.ElementId=t.ElementId
                WHERE t.GuideId=? AND t.StepId=?"""),
            ("ANCHORS", """SELECT a.ElementId,a.AnchorElementId,a.Relation,e.LocatorsJson
                FROM ElementAnchors a JOIN Elements e ON e.GuideId=a.GuideId AND e.ElementId=a.AnchorElementId
                WHERE a.GuideId=? AND a.ElementId IN
                (SELECT ElementId FROM StepTargets WHERE GuideId=? AND StepId=?)"""),
            ("VARIABLE ACTIONS", """SELECT StepId,ActionId,VariableId,ActionKind,Timing,TargetUsageId,Property
                FROM StepVariableActions WHERE GuideId=? AND StepId=?"""),
        ]:
            params = (guide,guide,sid) if label == "ANCHORS" else (guide,sid)
            print(label, json.dumps(rows(sql,params),ensure_ascii=False,indent=2))
        print("VARIABLE DEFINITIONS",json.dumps(rows(
            "SELECT VariableId,DisplayName FROM GuideVariables WHERE GuideId=?",(guide,)),
            ensure_ascii=False,indent=2))
