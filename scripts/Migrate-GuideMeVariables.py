#!/usr/bin/env python3
"""One-time V5 capture-to-variable migration.

Default: read-only preview. --apply requires an EXPLICIT
integration switch to the unified variables runtime. This script is not
part of normal application startup or publishing.
"""
import argparse
import json
import pathlib
import re
import sqlite3
import sys

TOKEN = re.compile(r"\{\{step:([^}:]+):capture\}\}")
TABLES = """
CREATE TABLE IF NOT EXISTS GuideVariables (
 GuideId INTEGER NOT NULL, VariableId TEXT NOT NULL,
 DisplayName TEXT NOT NULL, InitialValue TEXT,
 PRIMARY KEY (GuideId, VariableId), UNIQUE (GuideId, DisplayName),
 FOREIGN KEY (GuideId) REFERENCES Guides(GuideId)
);
CREATE TABLE IF NOT EXISTS StepVariableActions (
 GuideId INTEGER NOT NULL, StepId TEXT NOT NULL,
 ActionId TEXT NOT NULL, ActionOrder INTEGER NOT NULL,
 VariableId TEXT NOT NULL, ActionKind TEXT NOT NULL CHECK (ActionKind IN
    ('SetValue','ReadFromTarget','Clear','CopyFromVariable')),
 LiteralValue TEXT, SourceVariableId TEXT, TargetUsageId TEXT,
 Property TEXT, Pattern TEXT,
 Timing TEXT NOT NULL CHECK (Timing IN ('BeforeAction','DuringStep','AfterAction')),
 PRIMARY KEY (GuideId, StepId, ActionId),
 UNIQUE (GuideId,StepId,ActionOrder),
 UNIQUE (GuideId, StepId, ActionOrder),
 FOREIGN KEY (GuideId, StepId) REFERENCES Steps(GuideId, StepId),
 FOREIGN KEY (GuideId, VariableId) REFERENCES GuideVariables(GuideId, VariableId),
 FOREIGN KEY (GuideId, SourceVariableId) REFERENCES GuideVariables(GuideId, VariableId),
 FOREIGN KEY (GuideId, StepId, TargetUsageId) REFERENCES StepTargets(GuideId, StepId, UsageId)
);
"""

def convert(db, apply):
    captures = db.execute("""
      SELECT r.GuideId,r.StepId,r.RuleId,r.TargetUsageId,r.Property,r.Pattern,r.Timing,
             e.Runtime
      FROM StepRules r JOIN StepTargets t
        ON t.GuideId=r.GuideId AND t.StepId=r.StepId AND t.UsageId=r.TargetUsageId
      JOIN Elements e ON e.GuideId=t.GuideId AND e.ElementId=t.ElementId
      WHERE r.RuleType='Capture'
      ORDER BY r.GuideId,r.StepId,r.RuleId
    """).fetchall()
    # Old runtime has one capture value per step. A non-unique source cannot
    # be converted to one variable without redefining its semantics.
    by_step = {}
    for row in captures:
        key = (row[0], row[1])
        if key in by_step:
            raise ValueError(f"Multiple legacy captures in {key}; resolve explicitly")
        by_step[key] = row
    updates = []
    references = set()
    for table, column in (("Elements", "LocatorsJson"),
                          ("Elements", "FrameContextJson"),
                          ("Steps", "ContextValue"),
                          ("StepRules", "ExpectedValue"),
                          ("StepRules", "OptionsJson")):
        # Rowid is safe for the one-time conversion of this SQLite database.
        for rowid, guide, value in db.execute(
            f"SELECT rowid,GuideId,{column} FROM {table} WHERE {column} LIKE '%{{{{step:%:capture}}}}%'"
        ):
            def subst(match):
                key = (guide, match.group(1))
                references.add(key)
                if key not in by_step:
                    raise ValueError(f"Unresolved capture reference {key} in {table}.{column}")
                return "{{var:" + variable_id(match.group(1)) + "}}"
            updated = TOKEN.sub(subst, value)
            updates.append((table, column, rowid, updated))
    for key in by_step:
        guide, step = key
        if not db.execute("SELECT 1 FROM Steps WHERE GuideId=? AND StepId=?",(guide,step)).fetchone():
            raise ValueError(f"Missing capture step {key}")
    print(f"Legacy captures: {len(captures)}; reference occurrences: {len(updates)}")
    for row in captures:
        guide,step,rule,usage,prop,pattern,timing,runtime=row
        resolved_timing = timing or ("BeforeAction" if runtime=="Web" else "DuringStep")
        print(f" guide={guide} step={step} variable={variable_id(step)} property={prop} timing={resolved_timing}")
        # Legacy Windows can observe during the step and after validation.
        # Preserve both observations as two ordered actions, not a second engine.
        if runtime=="Windows" and timing is None:
            print("  Windows implicit timing -> DuringStep + AfterAction actions")
    if not apply:
        print("Read-only preview; database unchanged")
        return
    # sqlite3.executescript() implicitly commits any pending transaction.
    # Execute each CREATE inside BEGIN IMMEDIATE to keep migration atomic.
    for statement in TABLES.split(";"):
        if statement.strip():
            db.execute(statement)
    for row in captures:
        guide,step,rule,usage,prop,pattern,timing,runtime=row
        vid=variable_id(step)
        db.execute("INSERT INTO GuideVariables(GuideId,VariableId,DisplayName) VALUES(?,?,?)",
                   (guide,vid,"לכידה: "+step))
        moments = ([timing] if timing else
                   ["BeforeAction"] if runtime=="Web" else
                   ["DuringStep","AfterAction"])
        for order,moment in enumerate(moments):
            db.execute("""INSERT INTO StepVariableActions
              (GuideId,StepId,ActionId,ActionOrder,VariableId,ActionKind,
               TargetUsageId,Property,Pattern,Timing)
              VALUES (?,?,?,?,?,'ReadFromTarget',?,?,?,?)""",
              (guide,step,"converted-"+rule+"-"+moment,order,vid,usage,prop,pattern,moment))
    for table,column,rowid,value in updates:
        db.execute(f"UPDATE {table} SET {column}=? WHERE rowid=?",(value,rowid))
    db.execute("DELETE FROM StepRules WHERE RuleType='Capture'")
    # Rebuild the validation table without the legacy Capture rule kind or
    # columns. SQLite cannot remove a value from an existing CHECK constraint.
    columns = {col[1] for col in db.execute("PRAGMA table_info(StepRules)")}
    if "Property" in columns:
        db.execute("""
            CREATE TABLE StepRulesCanonical (
                GuideId INTEGER NOT NULL,
                StepId TEXT NOT NULL,
                RuleType TEXT NOT NULL CHECK (RuleType IN
                    ('PrimaryValidation','AdditionalCompletionCondition')),
                RuleId TEXT NOT NULL,
                Kind TEXT NOT NULL,
                TargetUsageId TEXT,
                ExpectedValue TEXT,
                OptionsJson TEXT,
                PRIMARY KEY (GuideId,StepId,RuleType,RuleId),
                FOREIGN KEY (GuideId,StepId) REFERENCES Steps(GuideId,StepId),
                FOREIGN KEY (GuideId,StepId,TargetUsageId)
                    REFERENCES StepTargets(GuideId,StepId,UsageId)
            )
        """)
        db.execute("""INSERT INTO StepRulesCanonical
            (GuideId,StepId,RuleType,RuleId,Kind,TargetUsageId,ExpectedValue,OptionsJson)
            SELECT GuideId,StepId,RuleType,RuleId,Kind,TargetUsageId,ExpectedValue,OptionsJson
            FROM StepRules""")
        db.execute("DROP TABLE StepRules")
        db.execute("ALTER TABLE StepRulesCanonical RENAME TO StepRules")
    bad=db.execute("PRAGMA foreign_key_check").fetchall()
    if bad: raise ValueError(f"FK violations: {bad}")
    print("Conversion staged; transaction will commit")

def variable_id(step_id):
    # Preserve arbitrary source IDs losslessly; stable across runs.
    return "capture-" + step_id.encode("utf-8").hex()

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("database",type=pathlib.Path)
    parser.add_argument("--apply",action="store_true")
    parser.add_argument("--unified-runtime-ready",action="store_true",
                        help="Confirm the old Capture engine was removed in deployed code")
    args=parser.parse_args()
    if not args.database.is_file():
        parser.error("V5 database not found")
    if args.apply and not args.unified_runtime_ready:
        parser.error("--apply requires --unified-runtime-ready; never migrate ahead of runtime switch")
    connection=sqlite3.connect(str(args.database) if args.apply else
        f"file:{args.database.as_posix()}?mode=ro",uri=not args.apply)
    try:
        connection.execute("PRAGMA foreign_keys=ON")
        if args.apply: connection.execute("BEGIN IMMEDIATE")
        convert(connection,args.apply)
        if args.apply: connection.commit()
    except Exception:
        if args.apply: connection.rollback()
        raise
    finally:
        connection.close()

if __name__=="__main__":
    try: main()
    except (ValueError,sqlite3.Error) as error:
        print(f"Migration blocked: {error}",file=sys.stderr)
        sys.exit(1)
