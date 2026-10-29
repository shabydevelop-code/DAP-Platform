"""Read-only comparison of two GuideMe V5 steps, including resolved element definitions."""
import argparse
import json
import sqlite3

p = argparse.ArgumentParser()
p.add_argument("--db", default=r"C:\ProgramData\GuideMe\Data\GuideMe-V5.db")
p.add_argument("--guide", type=int, required=True)
p.add_argument("--steps", type=int, nargs=2, default=[22, 46])
args = p.parse_args()

with sqlite3.connect("file:" + args.db.replace("\\", "/") + "?mode=ro", uri=True) as db:
    db.row_factory = sqlite3.Row
    def get(sql, params=()):
        return [dict(r) for r in db.execute(sql, params)]
    def table(name, where, params):
        return get(f"SELECT * FROM {name} WHERE {where}", params)
    def decode(value):
        if not isinstance(value, str):
            return value
        try:
            return json.loads(value)
        except (ValueError, TypeError):
            return value
    def describe(order):
        steps = table("Steps", "GuideId=? AND StepOrder=?", (args.guide, order))
        if len(steps) != 1:
            raise SystemExit(f"Expected one step at order {order}; found {len(steps)}")
        step = steps[0]
        sid = step["StepId"]
        targets = table("StepTargets", "GuideId=? AND StepId=?", (args.guide, sid))
        elements = []
        visited = set()
        def add_element(eid, role):
            if eid in visited:
                return
            visited.add(eid)
            records = table("Elements", "GuideId=? AND ElementId=?", (args.guide, eid))
            for e in records:
                elements.append({"role": role, "display": e.get("DisplayName"),
                    "runtime": e.get("Runtime"), "locators": decode(e.get("LocatorsJson")),
                    "frame": decode(e.get("FrameContextJson"))})
            anchors = table("ElementAnchors", "GuideId=? AND ElementId=?", (args.guide, eid))
            for a in anchors:
                add_element(a["AnchorElementId"], "anchor")
        for t in targets:
            add_element(t["ElementId"], t.get("Purpose"))
        contexts = table("ApplicationContexts", "GuideId=? AND ContextId=?",
                         (args.guide, step["ApplicationContextId"]))
        rules = table("StepRules", "GuideId=? AND StepId=?", (args.guide, sid))
        return {"step": {k: decode(v) for k,v in step.items() if k not in ("StepId", "StepOrder")},
                "context": contexts, "targets": [{k:v for k,v in t.items() if k not in ("StepId","ElementId")} for t in targets],
                "elements": elements,
                "rules": [{k:decode(v) for k,v in r.items() if k != "StepId"} for r in rules]}
    a,b = (describe(n) for n in args.steps)
    for label, data in zip(args.steps, (a,b)):
        print(f"\n=== STEP {label} ===")
        print(json.dumps(data, ensure_ascii=False, indent=2, default=str))
    print("\n=== DIFFERENCES (IDs excluded from element definitions) ===")
    for key in a:
        if a[key] != b[key]:
            print(f"{key}: DIFFERENT")
        else:
            print(f"{key}: identical")
    print("\nRead-only. No database changes were made.")
