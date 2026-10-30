# GuideMe — unified guide variables

## Decision
- One **guide-level variable catalog**, one **ordered step action list**, and one **runtime variable store** per learner session.
- The Instructor defines variables alongside Web/Windows application contexts, using user-facing names and automatic stable IDs.
- A step can set a literal value, read from the current target, clear a value, or copy another variable. A picker supplies the target; manual locator and expression editing are not the standard authoring experience.
- Runtime variable references are stable IDs chosen through the Instructor UI, not names or prior step IDs.
- The Core owns state/ordering/substitution. Web and Windows adapters only read a value from a selected element or supported runtime property.
- Values are session-local and must not be persisted as definitions or shared across sessions.
- No permanent compatibility reader or dual engine for legacy StepRules Capture or `{{step:...:capture}}`.

## Implemented code (Windows build / Instructor authoring regression pending)
- The sole execution path uses GuideVariableSession in GuideRunPlan and GuideExecutionEngine for both single-runtime and mixed Web/Windows guides.
- GuideV5ExecutionCompiler reads guide variables and ordered step actions. Source targets reuse StepTargets / Elements and preserve Web iframe and Windows UIA descriptors.
- BeforeAction and AfterAction assignments execute at shared step boundaries; DuringStep is an adapter observation feeding the same variable session.
- The Web adapter uses the existing DOM reading transport and the Windows adapter uses UI Automation; neither owns guide variable storage.
- GuideStep no longer stores legacy Capture. V5 no longer compiles legacy StepRules Capture, and the new schema does not accept that rule type.
- The Instructor supports guide-level add/edit/remove, step-level set/read/clear/copy, and binding a picked primary target's locator value from a variable selection.
- `scripts/Migrate-GuideMeVariables.py` converts existing captures and references once. Windows implicit dual sampling becomes two actions (DuringStep and AfterAction); the migration recreates StepRules without Capture fields.
- The Instructor source-picker extraction selector is now populated from the chosen element's observed value. Authors may store the full runtime value, the first numeric portion, or a custom regular-expression capture. Only the expression (Pattern) is persisted in StepVariableActions; captured preview values are not stored in guide definitions. The runtime re-reads the live value each session.
- Independent source element selection uses the existing Instructor Web/Windows picker and persists a dedicated `VariableSource` StepTarget/Element plus ordered `ReadFromTarget` action transactionally, without changing the primary/completion targets.
- The Instructor exposes one **Add variable action** button. Choosing a target read opens the same Web/Windows picker; other action kinds save directly. Manual reorder UI/API/code and the older direct-read form have been deleted. New authored actions append in creation order. Newly picked reads use fixed `AfterAction` timing: the shared engine runs them only after the adapter returns from an actually completed step, including its primary validation and additional completion conditions. Other manual actions also use `AfterAction`; the existing runtime timing engine and historical migrated timings remain intact (some legacy captures intentionally run `BeforeAction` to preserve the value before navigation).
- When a dedicated source read action is deleted, its unreferenced target and element are cleaned up. A primary target can be selected after defining a source-only target.
- The user migrated the V5 database successfully (GuideVariables=2, StepVariableActions=3, legacy Capture rules=0, foreign key errors=0) and published Learner and Instructor. Web Learner full regression succeeded after fixing iframe context for variable URL fragment reads. Windows/mixed runtime regressions and the new Instructor authoring flow are still pending; a Windows build of the new Instructor changes has not yet been reported.

## One-time migration and verification

1. Close Instructor and Learner processes and **build the updated solution first**. Do not migrate a database while old binaries are active.
2. Preview in read-only mode:
   `python scripts/Migrate-GuideMeVariables.py "C:\\ProgramData\\GuideMe\\Data\\GuideMe-V5.db"`
3. Compare the preview with expected legacy capture counts and references. The migration is not an application startup hook.
4. Apply once, with the unified code built, using
   `python scripts/Migrate-GuideMeVariables.py "C:\\ProgramData\\GuideMe\\Data\\GuideMe-V5.db" --apply --unified-runtime-ready`.
   The script validates foreign keys; it does not create a backup (per user request).
5. Re-publish updated GuideMe app and Instructor, then validate all original enabled Web and Windows steps and a mixed guide.
6. Regression checkpoints: original Web 10 save, 11 captured frame hash, 12 reopen matching new case, 48/50 reuse; Windows captured ID and name; multiple actions on one step; variable reset/copy; and stop/restart session variable isolation.

## Release gate
Do not claim full completion until updated .NET sources build on Windows, the Instructor variable-picker/reorder authoring path is exercised end-to-end, and Windows/mixed guide regressions pass. Web learner full regression and migration were reported successful. A guide tested from a later step can legitimately lack variables initialized in earlier steps; surface that as a missing variable rather than guessing.


## Variable action scope (2026-10-30)

- A step can update a guide variable only by reading a selected application target (`ReadFromTarget`), after its action and completion validation.
- The Instructor variable-action dialog selects the destination variable and proceeds directly to target picking; it has no operation-kind dropdown.
- Constant assignment, clearing a variable, and copying from another variable are not supported in the Instructor API, shared action enum, or Learner execution logic. Existing records of these unsupported action kinds are not migrated or supported.
- Both Web and Windows use the same shared action model and capture ordering.

## Unassigned values in Learner (2026-10-30)

- A variable with a null session value remains unassigned internally; do not write a placeholder to the database or change the session value.
- During variable interpolation (bubble text, CSS/Windows locators, expected validation values, completion rules), the shared `GuideVariableSession.Resolve` substitutes `לא הוקצה ערך` for Hebrew and `Value not assigned` for English.
- Language is supplied by the application host to the shared execution plan for Web, Windows, and mixed guides.
- No exception is raised merely because a defined variable has not yet been assigned. Missing targets and unsatisfied validation rules retain their normal waiting behavior. References to nonexistent variable IDs remain errors.
