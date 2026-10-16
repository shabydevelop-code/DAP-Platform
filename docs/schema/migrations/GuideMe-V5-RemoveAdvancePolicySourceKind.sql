-- GuideMe V5: one-time migration for existing databases.
-- Run only after backing up the database and stopping GuideMe processes.
-- Requires SQLite 3.35.0 or newer (DROP COLUMN support).
-- This script is intentionally not repeatable: do not run twice.
-- No production database is changed by committing this file.
PRAGMA foreign_keys=ON;
BEGIN IMMEDIATE;
ALTER TABLE StepAdvancePolicies DROP COLUMN SourceKind;
COMMIT;
PRAGMA integrity_check;
PRAGMA foreign_key_check;
