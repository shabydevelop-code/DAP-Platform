-- GuideMe V5 consolidated legacy metadata cleanup.
-- Apply once to an existing V5 database AFTER the earlier SourceKind migration.
-- Stop GuideMe and make a verified backup before running.
-- Requires SQLite >= 3.35.0. Not idempotent.
PRAGMA foreign_keys=ON;
BEGIN IMMEDIATE;
ALTER TABLE GuideElements DROP COLUMN SourceStepId;
ALTER TABLE StepObservedSignals DROP COLUMN DefinitionSource;
COMMIT;
PRAGMA integrity_check;
PRAGMA foreign_key_check;
