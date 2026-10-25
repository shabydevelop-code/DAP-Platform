# GuideMe — unified guide variables (implementation in progress)

## Decision
- One **guide-level variable catalog**, one **ordered step action list**, and one **runtime variable store** per learner session.
- The Instructor defines variables alongside Web/Windows application contexts, using user-facing names and automatic stable IDs.
- A step can set a literal value, read from the current target, clear a value, or copy another variable. A picker supplies the target; manual locator and expression editing are not the standard authoring experience.
- Runtime variable references are stable IDs chosen through the Instructor UI, not names or prior step IDs.
- The Core owns state/ordering/substitution. Web and Windows adapters only read a value from a selected element or supported runtime property.
- Values are session-local and must not be persisted as definitions or shared across sessions.
- No permanent compatibility reader or dual engine for legacy StepRules Capture or `{{step:...:capture}}`.

## Implemented code (not yet build-verified)
- The sole execution path uses GuideVariableSession in GuideRunPlan and GuideExecutionEngine for both single-runtime and mixed Web/Windows guides.
- GuideV5ExecutionCompiler reads guide variables and ordered step actions. Source targets reuse StepTargets / Elements and preserve Web iframe and Windows UIA descriptors.
- BeforeAction and AfterAction assignments execute at shared step boundaries; DuringStep is an adapter observation feeding the same variable session.
- The Web adapter uses the existing DOM reading transport and the Windows adapter uses UI Automation; neither owns guide variable storage.
- GuideStep no longer stores legacy Capture. V5 no longer compiles legacy StepRules Capture, and the new schema does not accept that rule type.
- The Instructor supports guide-level add/edit/remove, step-level set/read/clear/copy, timing, and binding a picked primary target's locator value from a variable selection.
- `scripts/Migrate-GuideMeVariables.py` converts existing captures and references once. Windows implicit dual sampling becomes two actions (DuringStep and AfterAction); the migration recreates StepRules without Capture fields.
- Target selection for a **different** source element than the step primary/completion target is not yet integrated into the Inspector picker.
- The database on the user's computer has **not** been migrated. A Windows build and full regression have **not** been run in this environment.

## One-time migration and verification

1. Close Instructor and Learner processes and **build the updated solution first**. Do not migrate a database while old binaries are active.
2. Preview in read-only mode:
   `python scripts/Migrate-GuideMeVariables.py "C:\\ProgramData\\GuideMe\\Data\\GuideMe-V5.db"`
3. Compare the preview with expected legacy capture counts and references. The migration is not an application startup hook.
4. Apply once, with the unified code built, using
   `python scripts/Migrate-GuideMeVariables.py "C:\\ProgramData\\GuideMe\\Data\\GuideMe-V5.db" --apply --unified-runtime-ready`.
   The script creates a SQLite backup before applying any changes and validates foreign keys.
5. Re-publish updated GuideMe app and Instructor, then validate all original enabled Web and Windows steps and a mixed guide.
6. Regression checkpoints: original Web 10 save, 11 captured frame hash, 12 reopen matching new case, 48/50 reuse; Windows captured ID and name; multiple actions on one step; variable reset/copy; and stop/restart session variable isolation.

## Release gate
Do not claim completion until updated .NET sources build on Windows, migration preview/apply is verified on a backup, and Web/Windows/mixed guide regressions pass. A guide tested from a later step can legitimately lack variables initialized in earlier steps; surface that as a missing variable rather than guessing.

