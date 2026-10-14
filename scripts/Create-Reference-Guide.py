"""Create a separate reference-guide baseline from the existing mixed guide.

Read-only by default. Use --apply to create a new guide; never overwrite.
"""
import argparse
import sqlite3
from pathlib import Path

SOURCE = 'sampleapp-mixed-smoke-guide'
TARGET = 'sampleapp-reference-guide'
DEFAULT_DB = r'C:\ProgramData\GuideMe\Data\GuideMe.db'


def copy_row(conn, table, row, changes):
    fields = [item[1] for item in conn.execute(f'PRAGMA table_info({table})') if item[1] != 'Id']
    values = [changes.get(field, row[field]) for field in fields]
    cols = ', '.join(fields)
    marks = ', '.join('?' for _ in fields)
    return conn.execute(f'INSERT INTO {table} ({cols}) VALUES ({marks})', values).lastrowid


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--db', default=DEFAULT_DB)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    db = Path(args.db)
    if not db.is_file():
        parser.error(f'Database not found: {db}')
    uri = db.resolve().as_uri() + ('?mode=rw' if args.apply else '?mode=ro')
    with sqlite3.connect(uri, uri=True, timeout=5) as conn:
        conn.row_factory = sqlite3.Row
        source = conn.execute('SELECT * FROM Guides WHERE Key=?', (SOURCE,)).fetchone()
        if source is None:
            raise ValueError('Source guide not found')
        if conn.execute('SELECT 1 FROM Guides WHERE Key=?', (TARGET,)).fetchone():
            raise ValueError('Destination guide already exists; refusing to overwrite')
        steps = conn.execute('SELECT * FROM GuideSteps WHERE GuideId=? ORDER BY StepOrder', (source['Id'],)).fetchall()
        contexts = conn.execute('SELECT * FROM GuideApplicationContexts WHERE GuideId=?', (source['Id'],)).fetchall()
        if not steps or not contexts or {s['Runtime'] for s in steps if s['Runtime']} != {'Web', 'Windows'}:
            raise ValueError('Expected a populated mixed-runtime source guide')
        print(f'Source: {len(steps)} steps, {len(contexts)} contexts')
        if not args.apply:
            print('Preview only. Run with --apply to create the separate guide.')
            return
        conn.execute('BEGIN IMMEDIATE')
        new_guide_id = copy_row(conn, 'Guides', source, {'Key': TARGET, 'Name': 'SampleApp Reference Guide'})
        for context in contexts:
            new_context_id = copy_row(conn, 'GuideApplicationContexts', context, {'GuideId': new_guide_id})
            for matcher in conn.execute('SELECT * FROM ApplicationContextMatchers WHERE ApplicationContextId=?', (context['Id'],)).fetchall():
                copy_row(conn, 'ApplicationContextMatchers', matcher, {'ApplicationContextId': new_context_id})
        for step in steps:
            new_step_id = copy_row(conn, 'GuideSteps', step, {'GuideId': new_guide_id})
            for table in ('TargetAnchors', 'StepCaptures', 'StepCompletionConditions'):
                for child in conn.execute(f'SELECT * FROM {table} WHERE GuideStepId=?', (step['Id'],)).fetchall():
                    copy_row(conn, table, child, {'GuideStepId': new_step_id})
        count = conn.execute('SELECT COUNT(*) FROM GuideSteps WHERE GuideId=?', (new_guide_id,)).fetchone()[0]
        if count != len(steps):
            raise RuntimeError('Copied step count mismatch')
        conn.commit()
        print(f'Created {TARGET}: {count} steps')


if __name__ == '__main__':
    main()
