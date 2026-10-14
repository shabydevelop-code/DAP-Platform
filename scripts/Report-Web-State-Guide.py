"""Read-only status and integrity report for the isolated Web state Guide."""
import argparse
import sqlite3
from collections import Counter
from pathlib import Path

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--db", default=r"C:\ProgramData\GuideMe\Data\GuideMe.db")
    a = p.parse_args()
    path = Path(a.db)
    if not path.is_file():
        p.error(f"Missing database: {path}")
    with sqlite3.connect(path.resolve().as_uri() + "?mode=ro", uri=True, timeout=5) as conn:
        conn.row_factory = sqlite3.Row
        keys = ("sampleapp-web-guide", "sampleapp-web-state-guide")
        ids = dict(conn.execute("SELECT Key, Id FROM Guides WHERE Key IN (?, ?)", keys))
        if len(ids) != 2:
            p.error("Original and cloned Guides must both exist")
        original = list(conn.execute("SELECT StepOrder, Key, ValidationKind, AdvanceMode FROM GuideSteps WHERE GuideId=? ORDER BY StepOrder", (ids[keys[0]],)))
        clone = list(conn.execute("SELECT StepOrder, Key, ValidationKind, AdvanceMode FROM GuideSteps WHERE GuideId=? ORDER BY StepOrder", (ids[keys[1]],)))
        if len(original) != 55 or len(clone) != 55:
            p.error(f"Unexpected step counts: original={len(original)}, clone={len(clone)}")
        for source, dest in zip(original, clone):
            if source["StepOrder"] != dest["StepOrder"] or dest["Key"] != source["Key"].replace("testcrm-", "sampleapp-", 1):
                p.error(f"Step mismatch at {source['StepOrder']}")
        counts = Counter(r["ValidationKind"] if r["AdvanceMode"] != "Manual" else "Manual" for r in clone)
        print(f"Original: {keys[0]} (Id={ids[keys[0]]}); 55 steps")
        print(f"Clone: {keys[1]} (Id={ids[keys[1]]}); 55 steps")
        for kind, count in sorted(counts.items()):
            print(f"{kind or 'none'}: {count}")
        remaining = [r["StepOrder"] for r in clone if r["ValidationKind"] == "clicked" and r["AdvanceMode"] != "Manual"]
        print(f"Still click-based ({len(remaining)}): {remaining}")
        print("Read-only verification. State transitions are not E2E-validated.")

if __name__ == "__main__":
    main()
