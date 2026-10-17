#!/usr/bin/env python3
"""Migrate GuideMe-V5.db in place from 11 to 7 tables.

No backup, no second database, no temporary tables. One SQLite transaction:
any failure rolls back the entire migration. Stop GuideMe before running.
"""
import json
import sqlite3
from collections import defaultdict
from pathlib import Path

DB = Path(r"C:\ProgramData\GuideMe\Data\GuideMe-V5.db")
SCHEMA = Path(__file__).resolve().parents[1] / "docs/schema/GuideMe-V5.sql"
OLD = (
    "GuideApplicationContexts", "ApplicationContextMatchers", "GuideSteps",
    "StepBubbles", "GuideElements", "ElementLocators",
    "ElementAnchorRelations", "StepElementUsages", "StepCaptures",
    "StepObservedSignals",
)
NEW = ("ApplicationContexts", "Steps", "Elements", "ElementAnchors", "StepTargets", "StepRules")
DROP_ORDER = (
    "StepCaptures", "StepObservedSignals", "StepElementUsages",
    "ElementAnchorRelations", "ElementLocators", "StepBubbles",
    "GuideSteps", "ApplicationContextMatchers", "GuideElements",
    "GuideApplicationContexts",
)


def rows(db, table, order=""):
    sql = f"SELECT * FROM {table}" + (f" ORDER BY {order}" if order else "")
    return [dict(r) for r in db.execute(sql)]


def count(db, table):
    return db.execute(f"SELECT COUNT(*) FROM {table}").fetchone()[0]


def pack(items):
    return json.dumps(items, ensure_ascii=False, separators=(",", ":"))


def normalize_element_runtime(value):
    mapping = {"0": "Web", "1": "Windows", "Web": "Web", "Windows": "Windows", None: None}
    normalized = mapping.get(str(value) if value is not None else None)
    if normalized is None and value is not None:
        raise RuntimeError(f"Unsupported element Runtime: {value!r}")
    return normalized


def migrate(db):
    tables = {r[0] for r in db.execute("SELECT name FROM sqlite_master WHERE type='table'")}
    if not set(OLD).issubset(tables) or set(NEW).intersection(tables):
        raise RuntimeError("Expected legacy 11-table schema only; refusing partial/repeated migration")
    if "Guides" not in tables:
        raise RuntimeError("Guides table missing")
    if db.execute("PRAGMA foreign_key_check").fetchone():
        raise RuntimeError("Source foreign key violations")
    before = {t: count(db, t) for t in OLD}

    matchers = defaultdict(list)
    for r in rows(db, "ApplicationContextMatchers", "GuideId,ContextId,MatcherOrder"):
        matchers[(r["GuideId"], r["ContextId"])].append({
            "Order": r["MatcherOrder"], "Kind": r["Kind"], "Value": r["Value"]})
    locators = defaultdict(list)
    for r in rows(db, "ElementLocators", "GuideId,ElementId,LocatorId"):
        locators[(r["GuideId"], r["ElementId"])].append({
            "Strategy": r["Strategy"], "Value": r["Value"]})

    # The schema file is DDL only. Execute individual CREATE TABLE statements
    # (never executescript, which would implicitly commit the migration).
    statement = ""
    for line in SCHEMA.read_text(encoding="utf-8-sig").splitlines(keepends=True):
        if line.lstrip().startswith("--") or line.lstrip().startswith("PRAGMA "):
            continue
        statement += line
        if sqlite3.complete_statement(statement):
            sql = statement.strip()
            if sql:
                if not sql.upper().startswith("CREATE TABLE "):
                    raise RuntimeError("Unexpected statement in compact schema")
                if not sql.startswith("CREATE TABLE Guides "):
                    db.execute(sql)
            statement = ""
    if statement.strip():
        raise RuntimeError("Incomplete compact schema")

    for r in rows(db, "GuideApplicationContexts"):
        key = (r["GuideId"], r["ContextId"])
        db.execute("INSERT INTO ApplicationContexts VALUES (?,?,?,?)",
                   (*key, r["Runtime"], pack(matchers[key])))
    for r in rows(db, "GuideSteps"):
        b = db.execute("SELECT Content,Placement FROM StepBubbles WHERE GuideId=? AND StepId=?",
                       (r["GuideId"], r["StepId"])).fetchone()
        if b is None:
            raise RuntimeError(f"Missing bubble for {r['StepId']}")
        db.execute("INSERT INTO Steps VALUES (?,?,?,?,?,?,?,?,?)", (
            r["GuideId"], r["StepId"], r["StepOrder"], r["IsEnabled"],
            r["ApplicationContextId"], r["ContextKind"], r["ContextValue"],
            b["Content"], b["Placement"]))
    for r in rows(db, "GuideElements"):
        key = (r["GuideId"], r["ElementId"])
        if not locators[key]:
            raise RuntimeError(f"Missing locator for {key}")
        db.execute("INSERT INTO Elements VALUES (?,?,?,?,?)",
                   (*key, normalize_element_runtime(r["Runtime"]), pack(locators[key]), r["FrameContextJson"]))
    for r in rows(db, "ElementAnchorRelations"):
        db.execute("INSERT INTO ElementAnchors VALUES (?,?,?,?,?)",
                   (r["GuideId"], r["ElementId"], r["AnchorElementId"],
                    r["Relation"], r["AnchorOrder"]))
    for r in rows(db, "StepElementUsages"):
        db.execute("INSERT INTO StepTargets VALUES (?,?,?,?,?)",
                   (r["GuideId"], r["StepId"], r["UsageId"], r["ElementId"], r["Purpose"]))
    for r in rows(db, "StepObservedSignals"):
        db.execute("INSERT INTO StepRules "
                   "(GuideId,StepId,RuleType,RuleId,Kind,TargetUsageId,ExpectedValue,OptionsJson) "
                   "VALUES (?,?,?,?,?,?,?,?)",
                   (r["GuideId"], r["StepId"], r["SignalRole"], r["SignalId"],
                    r["Kind"], r["TargetUsageId"], r["ExpectedValue"], r["OptionsJson"]))
    for r in rows(db, "StepCaptures"):
        db.execute("INSERT INTO StepRules "
                   "(GuideId,StepId,RuleType,RuleId,TargetUsageId,Property,Pattern,Timing) "
                   "VALUES (?,?,'Capture',?,?,?,?,?)",
                   (r["GuideId"], r["StepId"], r["CaptureId"],
                    r["TargetUsageId"], r["Property"], r["Pattern"], r["Timing"]))

    expected = {
        "ApplicationContexts": before["GuideApplicationContexts"],
        "Steps": before["GuideSteps"],
        "Elements": before["GuideElements"],
        "ElementAnchors": before["ElementAnchorRelations"],
        "StepTargets": before["StepElementUsages"],
        "StepRules": before["StepObservedSignals"] + before["StepCaptures"],
    }
    for table, n in expected.items():
        if count(db, table) != n:
            raise RuntimeError(f"Row count mismatch for {table}")
    if sum(len(v) for v in matchers.values()) != before["ApplicationContextMatchers"]:
        raise RuntimeError("Matcher loss")
    if sum(len(v) for v in locators.values()) != before["ElementLocators"]:
        raise RuntimeError("Locator loss")

    # Exact field-level comparisons before dropping any legacy table.
    for r in rows(db, "GuideApplicationContexts"):
        x = db.execute("SELECT * FROM ApplicationContexts WHERE GuideId=? AND ContextId=?",
                       (r["GuideId"], r["ContextId"])).fetchone()
        assert x["Runtime"] == r["Runtime"]
        assert json.loads(x["MatchersJson"]) == matchers[(r["GuideId"], r["ContextId"])]
    for r in rows(db, "GuideElements"):
        x = db.execute("SELECT * FROM Elements WHERE GuideId=? AND ElementId=?",
                       (r["GuideId"], r["ElementId"])).fetchone()
        assert x["Runtime"] == normalize_element_runtime(r["Runtime"]) and x["FrameContextJson"] == r["FrameContextJson"]
        assert json.loads(x["LocatorsJson"]) == locators[(r["GuideId"], r["ElementId"])]
    for r in rows(db, "GuideSteps"):
        x = db.execute("SELECT * FROM Steps WHERE GuideId=? AND StepId=?",
                       (r["GuideId"], r["StepId"])).fetchone()
        assert all(x[k] == r[k] for k in
                   ("StepOrder", "IsEnabled", "ApplicationContextId", "ContextKind", "ContextValue"))
        b = db.execute("SELECT Content,Placement FROM StepBubbles WHERE GuideId=? AND StepId=?",
                       (r["GuideId"], r["StepId"])).fetchone()
        assert (x["BubbleContent"], x["BubblePlacement"]) == tuple(b)
    for old, new, cols in (
        ("ElementAnchorRelations", "ElementAnchors",
         ("GuideId", "ElementId", "AnchorElementId", "Relation", "AnchorOrder")),
        ("StepElementUsages", "StepTargets",
         ("GuideId", "StepId", "UsageId", "ElementId", "Purpose")),
    ):
        assert sorted(tuple(r[k] for k in cols) for r in rows(db, old)) == sorted(
            tuple(r[k] for k in cols) for r in rows(db, new))
    for r in rows(db, "StepObservedSignals"):
        x = db.execute("SELECT * FROM StepRules WHERE GuideId=? AND StepId=? "
                       "AND RuleType=? AND RuleId=?",
                       (r["GuideId"], r["StepId"], r["SignalRole"], r["SignalId"])).fetchone()
        assert x is not None and all(x[k] == r[k] for k in
                                     ("Kind", "TargetUsageId", "ExpectedValue", "OptionsJson"))
    for r in rows(db, "StepCaptures"):
        x = db.execute("SELECT * FROM StepRules WHERE GuideId=? AND StepId=? "
                       "AND RuleType='Capture' AND RuleId=?",
                       (r["GuideId"], r["StepId"], r["CaptureId"])).fetchone()
        assert x is not None and all(x[k] == r[k] for k in
                                     ("TargetUsageId", "Property", "Pattern", "Timing"))

    for table in DROP_ORDER:
        db.execute(f"DROP TABLE {table}")
    remaining = {r[0] for r in db.execute(
        "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")}
    if remaining != {"Guides", *NEW}:
        raise RuntimeError(f"Unexpected final tables: {remaining}")
    if db.execute("PRAGMA foreign_key_check").fetchone():
        raise RuntimeError("Compact foreign key violations")
    if db.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
        raise RuntimeError("Compact database integrity failed")
    return expected


def main():
    if not DB.is_file():
        raise SystemExit(f"Database not found: {DB}")
    db = sqlite3.connect(str(DB), timeout=5, isolation_level=None)
    db.row_factory = sqlite3.Row
    try:
        db.execute("PRAGMA busy_timeout=5000")
        db.execute("PRAGMA foreign_keys=OFF")
        db.execute("BEGIN IMMEDIATE")
        try:
            counts = migrate(db)
            db.execute("COMMIT")
        except BaseException:
            if db.in_transaction:
                db.execute("ROLLBACK")
            raise
        db.execute("PRAGMA foreign_keys=ON")
        for table, n in counts.items():
            print(f"PASS {table}: {n}")
        print("PASS: in-place 7-table migration committed")
    finally:
        db.close()


if __name__ == "__main__":
    main()
