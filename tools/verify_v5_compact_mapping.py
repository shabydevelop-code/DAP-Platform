#!/usr/bin/env python3
"""V5 -> compact V5 mapping verification. Source is always read-only.

Default: in-memory destination. Optional --output creates a new disposable test fixture.
Never overwrites an existing output file. No backup is created.
"""
import argparse
import json
import sqlite3
from collections import defaultdict
from pathlib import Path

SOURCE = r"C:\ProgramData\GuideMe\Data\GuideMe-V5.db"
SCHEMA = Path(__file__).resolve().parents[1] / "docs/schema/GuideMe-V5.sql"


def rows(conn, table, order=""):
    sql = f"SELECT * FROM {table}" + (f" ORDER BY {order}" if order else "")
    return [dict(row) for row in conn.execute(sql)]


def compact_json(values):
    return json.dumps(values, ensure_ascii=False, separators=(",", ":"))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, help="new disposable compact test database")
    args = parser.parse_args()
    if args.output is not None:
        output = args.output.resolve()
        if output.exists():
            parser.error("Output already exists; refusing to overwrite it.")
        if output == Path(SOURCE).resolve():
            parser.error("Output must not be the production database.")
    src = sqlite3.connect(f"file:{SOURCE}?mode=ro", uri=True, timeout=5)
    src.row_factory = sqlite3.Row
    dst = sqlite3.connect(str(output) if args.output is not None else ":memory:", timeout=5)
    dst.row_factory = sqlite3.Row
    try:
        src.execute("PRAGMA query_only=ON")
        dst.executescript(SCHEMA.read_text(encoding="utf-8-sig"))
        dst.execute("PRAGMA foreign_keys=ON")
        assert not src.execute("PRAGMA foreign_key_check").fetchall()

        matchers = defaultdict(list)
        for r in rows(src, "ApplicationContextMatchers", "GuideId,ContextId,MatcherOrder"):
            matchers[(r["GuideId"], r["ContextId"])].append(
                {"Order": r["MatcherOrder"], "Kind": r["Kind"], "Value": r["Value"]})
        locators = defaultdict(list)
        for r in rows(src, "ElementLocators", "GuideId,ElementId,LocatorId"):
            locators[(r["GuideId"], r["ElementId"])].append(
                {"Strategy": r["Strategy"], "Value": r["Value"]})

        with dst:
            for r in rows(src, "Guides"):
                dst.execute("INSERT INTO Guides VALUES (?,?,?)",
                            (r["GuideId"], r["GuideKey"], r["GuideName"]))
            for r in rows(src, "GuideApplicationContexts"):
                key = r["GuideId"], r["ContextId"]
                dst.execute("INSERT INTO ApplicationContexts VALUES (?,?,?,?)",
                            (*key, r["Runtime"], compact_json(matchers[key])))
            for r in rows(src, "GuideSteps"):
                b = src.execute(
                    "SELECT Content,Placement FROM StepBubbles WHERE GuideId=? AND StepId=?",
                    (r["GuideId"], r["StepId"])).fetchone()
                assert b is not None, ("Missing bubble", r["StepId"])
                dst.execute("INSERT INTO Steps VALUES (?,?,?,?,?,?,?,?,?)", (
                    r["GuideId"], r["StepId"], r["StepOrder"], r["IsEnabled"],
                    r["ApplicationContextId"], r["ContextKind"], r["ContextValue"],
                    b["Content"], b["Placement"]))
            for r in rows(src, "GuideElements"):
                key = r["GuideId"], r["ElementId"]
                assert locators[key], ("Missing locator", key)
                dst.execute("INSERT INTO Elements VALUES (?,?,?,?,?)", (
                    *key, r["Runtime"], compact_json(locators[key]),
                    r["FrameContextJson"]))
            for r in rows(src, "ElementAnchorRelations"):
                dst.execute("INSERT INTO ElementAnchors VALUES (?,?,?,?,?)", (
                    r["GuideId"], r["ElementId"], r["AnchorElementId"],
                    r["Relation"], r["AnchorOrder"]))
            for r in rows(src, "StepElementUsages"):
                dst.execute("INSERT INTO StepTargets VALUES (?,?,?,?,?)", (
                    r["GuideId"], r["StepId"], r["UsageId"], r["ElementId"], r["Purpose"]))
            for r in rows(src, "StepObservedSignals"):
                dst.execute(
                    "INSERT INTO StepRules "
                    "(GuideId,StepId,RuleType,RuleId,Kind,TargetUsageId,ExpectedValue,OptionsJson) "
                    "VALUES (?,?,?,?,?,?,?,?)", (
                        r["GuideId"], r["StepId"], r["SignalRole"], r["SignalId"],
                        r["Kind"], r["TargetUsageId"], r["ExpectedValue"], r["OptionsJson"]))
            for r in rows(src, "StepCaptures"):
                dst.execute(
                    "INSERT INTO StepRules "
                    "(GuideId,StepId,RuleType,RuleId,TargetUsageId,Property,Pattern,Timing) "
                    "VALUES (?,?,'Capture',?,?,?,?,?)", (
                        r["GuideId"], r["StepId"], r["CaptureId"],
                        r["TargetUsageId"], r["Property"], r["Pattern"], r["Timing"]))

        expected = {
            "Guides": ("Guides",),
            "ApplicationContexts": ("GuideApplicationContexts",),
            "Steps": ("GuideSteps",),
            "Elements": ("GuideElements",),
            "ElementAnchors": ("ElementAnchorRelations",),
            "StepTargets": ("StepElementUsages",),
            "StepRules": ("StepObservedSignals", "StepCaptures"),
        }
        for target, sources in expected.items():
            actual = dst.execute(f"SELECT COUNT(*) FROM {target}").fetchone()[0]
            wanted = sum(src.execute(f"SELECT COUNT(*) FROM {s}").fetchone()[0] for s in sources)
            assert actual == wanted, (target, actual, wanted)
            print(f"PASS {target}: {actual}/{wanted}")

        # Verify the entire persisted representation, not just aggregate counts.
        for r in rows(src, "GuideApplicationContexts"):
            x = dst.execute(
                "SELECT * FROM ApplicationContexts WHERE GuideId=? AND ContextId=?",
                (r["GuideId"], r["ContextId"])).fetchone()
            assert x["Runtime"] == r["Runtime"]
            assert json.loads(x["MatchersJson"]) == matchers[(r["GuideId"], r["ContextId"])]
        for r in rows(src, "GuideElements"):
            x = dst.execute("SELECT * FROM Elements WHERE GuideId=? AND ElementId=?",
                            (r["GuideId"], r["ElementId"])).fetchone()
            assert x["Runtime"] == r["Runtime"]
            assert x["FrameContextJson"] == r["FrameContextJson"]
            assert json.loads(x["LocatorsJson"]) == locators[(r["GuideId"], r["ElementId"])]
        for r in rows(src, "GuideSteps"):
            x = dst.execute("SELECT * FROM Steps WHERE GuideId=? AND StepId=?",
                            (r["GuideId"], r["StepId"])).fetchone()
            for a, b in (("StepOrder", "StepOrder"), ("IsEnabled", "IsEnabled"),
                         ("ApplicationContextId", "ApplicationContextId"),
                         ("ContextKind", "ContextKind"), ("ContextValue", "ContextValue")):
                assert x[a] == r[b], (r["StepId"], a)
            bubble = src.execute("SELECT Content,Placement FROM StepBubbles "
                                 "WHERE GuideId=? AND StepId=?",
                                 (r["GuideId"], r["StepId"])).fetchone()
            assert (x["BubbleContent"], x["BubblePlacement"]) == tuple(bubble)
        for original, target, mapping in [
            ("ElementAnchorRelations", "ElementAnchors",
             ("GuideId", "ElementId", "AnchorElementId", "Relation", "AnchorOrder")),
            ("StepElementUsages", "StepTargets",
             ("GuideId", "StepId", "UsageId", "ElementId", "Purpose")),
        ]:
            original_values = sorted(tuple(r[c] for c in mapping) for r in rows(src, original))
            new_values = sorted(tuple(r[c] for c in mapping) for r in rows(dst, target))
            assert original_values == new_values, original
        for r in rows(src, "StepObservedSignals"):
            x = dst.execute("SELECT * FROM StepRules WHERE GuideId=? AND StepId=? "
                            "AND RuleType=? AND RuleId=?",
                            (r["GuideId"], r["StepId"], r["SignalRole"], r["SignalId"])).fetchone()
            assert x is not None
            assert all(x[c] == r[c] for c in
                       ("Kind", "TargetUsageId", "ExpectedValue", "OptionsJson"))
        for r in rows(src, "StepCaptures"):
            x = dst.execute("SELECT * FROM StepRules WHERE GuideId=? AND StepId=? "
                            "AND RuleType='Capture' AND RuleId=?",
                            (r["GuideId"], r["StepId"], r["CaptureId"])).fetchone()
            assert x is not None
            assert all(x[c] == r[c] for c in
                       ("TargetUsageId", "Property", "Pattern", "Timing"))
        assert not dst.execute("PRAGMA foreign_key_check").fetchall()
        assert dst.execute("PRAGMA integrity_check").fetchone()[0] == "ok"
        print("PASS: full persisted-data equivalence and referential integrity")
        print("PASS: source database unchanged; destination:", str(output) if args.output is not None else "memory only")
    finally:
        dst.close()
        src.close()


if __name__ == "__main__":
    main()
