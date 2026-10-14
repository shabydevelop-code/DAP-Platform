"""Find a minimal, evidence-backed reference feature set from real persisted steps.

Does not modify the database. Outputs uncovered capabilities explicitly instead
of inflating step count by concatenating canonical guides.
"""
import argparse
import json
import sqlite3
from pathlib import Path

from importlib.util import spec_from_file_location, module_from_spec

HERE = Path(__file__).resolve().parent
spec = spec_from_file_location("reference_coverage", HERE / "Measure-Reference-Coverage.py")
coverage = module_from_spec(spec)
spec.loader.exec_module(coverage)

SOURCES = (("sampleapp-reference-guide", "Reference"),
           ("sampleapp-web-guide", "Web"),
           ("sampleapp-windows-guide", "Windows"))


def load(conn, key):
    row = conn.execute("SELECT Id FROM Guides WHERE Key=?", (key,)).fetchone()
    if row is None:
        raise ValueError(f"Missing guide: {key}")
    steps = [dict(x) for x in conn.execute(
        "SELECT * FROM GuideSteps WHERE GuideId=? ORDER BY StepOrder", (row[0],))]
    for step in steps:
        for table in ("TargetAnchors", "StepCaptures", "StepCompletionConditions"):
            step[table] = [dict(x) for x in conn.execute(
                f"SELECT * FROM {table} WHERE GuideStepId=?", (step["Id"],))]
    return steps


def features(step):
    found = coverage.observed([step])
    runtime = step["Runtime"]
    if runtime not in coverage.CATALOG:
        return set()
    return {f"{runtime}/{group}/{name}" for group, names in found.items()
            for name in names if name in coverage.CATALOG[runtime].get(group, [])}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--db", default=coverage.DEFAULT_DB)
    p.add_argument("--output", default="reference-feature-design.json")
    args = p.parse_args()
    db = Path(args.db)
    if not db.is_file():
        p.error(f"Database not found: {db}")
    with sqlite3.connect(db.resolve().as_uri() + "?mode=ro", uri=True, timeout=5) as conn:
        conn.row_factory = sqlite3.Row
        guides = {key: load(conn, key) for key, _ in SOURCES}

    universe = {f"{runtime}/{group}/{name}" for runtime, groups in coverage.CATALOG.items()
                for group, names in groups.items() for name in names}
    current = set().union(*(features(s) for s in guides[SOURCES[0][0]]))
    candidates = []
    for key, _ in SOURCES[1:]:
        for step in guides[key]:
            candidates.append({"guide": key, "step": step["StepOrder"],
                               "key": step["Key"], "runtime": step["Runtime"],
                               "features": features(step)})
    uncovered = universe - current
    chosen = []
    # Greedy set cover is a planning lower-bound heuristic, NOT a runnable sequence.
    while True:
        best = max(candidates, key=lambda x: len(x["features"] & uncovered), default=None)
        if not best:
            break
        gained = best["features"] & uncovered
        if not gained:
            break
        chosen.append({"sourceGuide": best["guide"], "sourceOrder": best["step"],
                       "sourceKey": best["key"], "newFeatures": sorted(gained)})
        uncovered -= gained
        candidates.remove(best)

    result = {
        "status": "DESIGN_ONLY_NOT_RUNNABLE_NOT_RUNTIME_VERIFIED",
        "referenceSteps": len(guides[SOURCES[0][0]]),
        "referenceConfigured": len(current & universe),
        "catalogSize": len(universe),
        "referenceMissing": sorted(universe - current),
        "candidateSourceSteps": chosen,
        "requiresNewTargetedScenarios": sorted(uncovered),
        "note": "Selected source steps are capability evidence, not a valid execution order. "
                "A new scenario must be authored for each uncovered feature and "
                "the complete sequence verified in the actual applications."
    }
    Path(args.output).write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Reference: {result['referenceSteps']} steps; {len(current & universe)}/{len(universe)} configured features")
    print(f"Reusable candidate examples: {len(chosen)}")
    print(f"Features requiring new scenarios: {len(uncovered)}")
    for feature in sorted(uncovered):
        print("  MISSING", feature)
    print("Design report:", args.output)


if __name__ == "__main__":
    main()
