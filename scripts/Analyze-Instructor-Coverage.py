"""Read-only coverage report for the two canonical GuideMe Guides."""
import importlib.util
import json
import sqlite3
from collections import Counter
from pathlib import Path

source = Path(__file__).with_name("Export-Guide-For-Instructor.py")
spec = importlib.util.spec_from_file_location("guide_export", source)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

db = Path(r"C:\ProgramData\GuideMe\Data\GuideMe.db")
uri = db.resolve().as_uri() + "?mode=ro"
with sqlite3.connect(uri, uri=True) as conn:
    conn.row_factory = sqlite3.Row
    guides = [module.export(conn, key) for key in ("sampleapp-web-guide", "sampleapp-windows-guide")]

def distribution(steps, field):
    return dict(sorted(Counter(str(step.get(field) or "(none)") for step in steps).items()))

report = {"Guides": {}, "TotalSteps": 0}
for guide in guides:
    steps = guide["Steps"]
    report["TotalSteps"] += len(steps)
    nested = {"Anchors": "TargetAnchors", "Captures": "StepCaptures",
              "CompletionConditions": "StepCompletionConditions"}
    entry = {"Name": guide["Guide"]["Name"], "StepCount": len(steps),
             "ContextCount": len(guide["ApplicationContexts"]),
             "Distributions": {field: distribution(steps, field) for field in
                ("Runtime", "LocatorStrategy", "ValidationKind", "AdvanceMode",
                 "BubblePlacement", "ContextKind", "ApplicationContextKey")},
             "Capabilities": {}}
    for label, field in nested.items():
        matching = [s for s in steps if s.get(field)]
        entry["Capabilities"][label] = {
            "StepOrders": [s["StepOrder"] for s in matching],
            "ItemCount": sum(len(s[field]) for s in matching),
            "Types": dict(sorted(Counter(str(item.get("Kind") or item.get("Relation") or item.get("Property") or "(none)")
                for s in matching for item in s[field]).items()))}
    for label, field in (("FrameContext", "FrameContextJson"),
                         ("StepContext", "ContextKind")):
        entry["Capabilities"][label] = {"StepOrders": [
            s["StepOrder"] for s in steps if s.get(field) is not None and s.get(field) != ""]}
    entry["Capabilities"]["Targetless"] = {"StepOrders": [
        s["StepOrder"] for s in steps if not s.get("LocatorStrategy")]}
    entry["ContextMatchers"] = dict(sorted(Counter(str(m["Kind"])
        for c in guide["ApplicationContexts"] for m in c.get("Matchers", [])).items()))
    report["Guides"][guide["Guide"]["Key"]] = entry

output = Path("instructor-capability-coverage.json")
output.write_text(json.dumps({"Coverage": report, "GuideExports": guides},
    ensure_ascii=False, indent=2, default=str), encoding="utf-8")
print(f"Exported {report['TotalSteps']} Steps to {output}")
print("Read-only analysis; observed usage is not the complete feature contract.")
