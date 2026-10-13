"""Create a mixed-runtime smoke Guide by copying real persisted SampleApp steps.

Usage:
    python scripts/Create-Mixed-Guide.py
Creates a new Guide only; never modifies source Guides. Requires both apps
to be running and in their initial state before the smoke run.
"""
import sqlite3
from pathlib import Path

DB = Path(r"C:\ProgramData\GuideMe\Data\GuideMe.db")
DEST = "sampleapp-mixed-smoke-guide"
SOURCES = [
    ("sampleapp-web-guide", (1, 2)),
    ("sampleapp-windows-guide", (1, 2)),
    ("sampleapp-web-guide", (3, 4)),
]
CHILD_TABLES = {
    "TargetAnchors": ("GuideStepId", "Id"),
    "StepCaptures": ("GuideStepId", None),
    "StepCompletionConditions": ("GuideStepId", "Id"),
}


def copy_row(conn, table, source_id, overrides, excluded=("Id",)):
    columns = [row[1] for row in conn.execute(f"PRAGMA table_info({table})")]
    payload = {
        col: value for col, value in zip(
            columns, conn.execute(f"SELECT * FROM {table} WHERE Id=?", (source_id,)).fetchone()
        ) if col not in excluded
    }
    payload.update(overrides)
    names = list(payload)
    conn.execute(
        f"INSERT INTO {table} ({','.join(names)}) VALUES ({','.join('?' for _ in names)})",
        [payload[name] for name in names],
    )
    return conn.execute("SELECT last_insert_rowid()").fetchone()[0]


def main():
    if not DB.is_file():
        raise SystemExit(f"Database not found: {DB}")
    with sqlite3.connect(DB) as conn:
        conn.execute("PRAGMA foreign_keys=ON")
        conn.execute("BEGIN IMMEDIATE")
        if conn.execute("SELECT 1 FROM Guides WHERE Key=?", (DEST,)).fetchone():
            raise SystemExit(f"Guide already exists: {DEST}; no changes made")
        source_ids = {}
        for key, _ in SOURCES:
            row = conn.execute("SELECT Id FROM Guides WHERE Key=?", (key,)).fetchone()
            if row is None:
                raise ValueError(f"Missing source Guide: {key}")
            source_ids[key] = row[0]
        dest_id = conn.execute(
            "INSERT INTO Guides(Key,Name) VALUES(?,?) RETURNING Id",
            (DEST, "SampleApp Mixed Web-Windows-Web Smoke"),
        ).fetchone()[0]
        context_keys = {}
        order = 0
        for source_key, source_orders in SOURCES:
            source_id = source_ids[source_key]
            for source_order in source_orders:
                source_step = conn.execute(
                    "SELECT Id,Key,ApplicationContextKey FROM GuideSteps WHERE GuideId=? AND StepOrder=?",
                    (source_id, source_order),
                ).fetchone()
                if source_step is None:
                    raise ValueError(f"Missing Step {source_order} in {source_key}")
                step_id, step_key, context_key = source_step
                if context_key and (source_key, context_key) not in context_keys:
                    source_context = conn.execute(
                        "SELECT Id FROM GuideApplicationContexts WHERE GuideId=? AND Key=?",
                        (source_id, context_key),
                    ).fetchone()
                    if source_context is None:
                        raise ValueError(f"Missing context {context_key} in {source_key}")
                    new_key = f"{'web' if 'web' in source_key else 'windows'}-{context_key}"
                    new_context_id = copy_row(
                        conn, "GuideApplicationContexts", source_context[0],
                        {"GuideId": dest_id, "Key": new_key},
                    )
                    for (matcher_id,) in conn.execute(
                        "SELECT Id FROM ApplicationContextMatchers WHERE ApplicationContextId=? ORDER BY MatcherOrder",
                        (source_context[0],),
                    ).fetchall():
                        copy_row(conn, "ApplicationContextMatchers", matcher_id,
                                 {"ApplicationContextId": new_context_id})
                    context_keys[(source_key, context_key)] = new_key
                order += 1
                new_step_id = copy_row(
                    conn, "GuideSteps", step_id,
                    {"GuideId": dest_id, "StepOrder": order,
                     "Key": f"{'web' if 'web' in source_key else 'windows'}-{step_key}",
                     "ApplicationContextKey": context_keys.get((source_key, context_key))},
                )
                for table, foreign_key, _ in (
                    (name, *definition) for name, definition in CHILD_TABLES.items()
                ):
                    columns = [r[1] for r in conn.execute(f"PRAGMA table_info({table})")]
                    for row in conn.execute(
                        f"SELECT * FROM {table} WHERE {foreign_key}=?", (step_id,)
                    ).fetchall():
                        payload = dict(zip(columns, row))
                        payload[foreign_key] = new_step_id
                        if table != "StepCaptures":
                            payload.pop("Id", None)
                        names = list(payload)
                        conn.execute(
                            f"INSERT INTO {table} ({','.join(names)}) VALUES ({','.join('?' for _ in names)})",
                            [payload[name] for name in names],
                        )
        runtimes = {
            row[0] for row in conn.execute(
                "SELECT DISTINCT Runtime FROM GuideSteps WHERE GuideId=? AND Runtime IS NOT NULL",
                (dest_id,),
            )
        }
        if runtimes != {"Web", "Windows"}:
            raise ValueError(f"Expected Web and Windows steps, got {runtimes}")
        print(f"Created {DEST}: {order} steps; runtimes: {sorted(runtimes)}")
        conn.commit()


if __name__ == "__main__":
    main()
