"""Read-only feasibility audit of the installed 55-step Web Guide."""
import sqlite3
from pathlib import Path

DB = Path(r"C:\ProgramData\GuideMe\Data\GuideMe.db")
KEY = "sampleapp-web-guide"

with sqlite3.connect(DB.resolve().as_uri() + "?mode=ro", uri=True, timeout=5) as conn:
    conn.row_factory = sqlite3.Row
    guide = conn.execute("SELECT Id, Name FROM Guides WHERE Key=?", (KEY,)).fetchone()
    if guide is None:
        raise SystemExit(f"Guide not found: {KEY}")
    steps = conn.execute("""
        SELECT s.Id, s.StepOrder, s.Key, s.AdvanceMode, s.Runtime,
               s.LocatorStrategy, s.LocatorValue, s.ValidationKind,
               (SELECT COUNT(*) FROM StepCompletionConditions c
                WHERE c.GuideStepId=s.Id) AS ConditionCount
        FROM GuideSteps s WHERE s.GuideId=? ORDER BY s.StepOrder
    """, (guide["Id"],)).fetchall()
    print(f"Guide: {guide['Name']} | Steps: {len(steps)}")
    print("Step | Key | Advance | Validation | Conditions | State-only feasibility")
    for s in steps:
        manual = s["AdvanceMode"] == "Manual"
        supported = manual or s["ConditionCount"] > 0
        verdict = "MANUAL" if manual else ("CONDITION_PRESENT" if supported else "NEEDS_EXPLICIT_CONDITION")
        print(f"{s['StepOrder']:>2} | {s['Key']} | {s['AdvanceMode']} | "
              f"{s['ValidationKind']} | {s['ConditionCount']} | {verdict}")
    missing = [s["StepOrder"] for s in steps
               if s["AdvanceMode"] != "Manual" and not s["ConditionCount"]]
    print(f"Missing explicit state completion: {len(missing)}; steps: {missing}")
    if len(steps) != 55:
        raise SystemExit("Unexpected step count; no changes made.")

    print("\\nDETAILED CONDITION REVIEW")
    for idx, step in enumerate(steps):
        conds = conn.execute("""SELECT Kind, ExpectedValue, TargetJson
                                 FROM StepCompletionConditions
                                 WHERE GuideStepId=? ORDER BY ConditionOrder""", (step["Id"],)).fetchall()
        if step["AdvanceMode"] == "Manual":
            continue
        if conds:
            for cond in conds:
                print(f"{step['StepOrder']:>2} {step['Key']}: {cond['Kind']} "
                      f"expected={cond['ExpectedValue']!r} target={cond['TargetJson']}")
        else:
            print(f"{step['StepOrder']:>2} {step['Key']}: MISSING; "
                  f"validation={step['ValidationKind']!r}; "
                  f"target={step['LocatorStrategy']}:{step['LocatorValue']}")
            if idx + 1 < len(steps):
                nxt = steps[idx + 1]
                print(f"   next={nxt['Key']} target={nxt['LocatorStrategy']}:{nxt['LocatorValue']}")
    print("\\nWARNING: Condition presence does not prove transition uniqueness.")
