# GuideMe — unified guide variables (implementation in progress)

## Decision
- One **guide-level variable catalog**, one **ordered step action list**, and one **runtime variable store** per learner session.
- The Instructor defines variables alongside Web/Windows application contexts, using user-facing names and automatic stable IDs.
- A step can set a literal value, read from the current target, clear a value, or copy another variable. A picker supplies the target; manual locator and expression editing are not the standard authoring experience.
- Runtime variable references are stable IDs chosen through the Instructor UI, not names or prior step IDs.
- The Core owns state/ordering/substitution. Web and Windows adapters only read a value from a selected element or supported runtime property.
- Values are session-local and must not be persisted as definitions or shared across sessions.
- No permanent compatibility reader or dual engine for legacy StepRules Capture or `{{step:...:capture}}`.

## Model and storage (first code changes)
- `src/GuideMe.Core/Guides/GuideVariableDefinition.cs`: definitions and step actions.
- `docs/schema/GuideMe-V5.sql`: `GuideVariables` and `StepVariableActions` with foreign keys to the existing guide, step, and target catalog.
- Added `GuideVariableSession` in Core: isolated per-run variable values, assign/copy/clear/read, and `{{var:<id>}}` substitution. It is not yet wired into learner execution.
- Added `scripts/Migrate-GuideMeVariables.py`: read-only default, creates a backup before explicit apply and blocks Windows implicit capture timings requiring an audit. The migration must not be applied until the unified engine fully replaces Capture.
- No database migration or active runtime switch has been executed yet.

## One-time conversion before enabling the new engine
1. Back up the existing V5 database.
2. Enumerate `StepRules` with `RuleType='Capture'`, and all locator/anchor/condition strings containing `{{step:<id>:capture}}`.
3. Create one stable guide variable for each captured value; convert each capture into a `ReadFromTarget` step action, preserving its property, pattern, timing, target usage and step.
4. Replace old step-based tokens in all persisted targets with stable variable references and verify every reference resolves to one guide variable.
5. Audit runtime timings: existing null capture timings are platform-specific. Normalize them explicitly during conversion, not during execution. Ensure frames and nested paths are preserved.
6. Run a read-only comparison of all original guide steps, runtime contexts, selectors and validation/completion rules, then perform production-like Web and Windows guide regressions.
7. Remove `Capture` from V5 rule types and Core step definitions, and delete the old runtime Capture code in the **same integration change** that enables the variable engine. Do not ship two enabled engines.
8. Validate the example sequence: save case (10), read URL fragment (11), reopen captured case (12), and reuse it (48/50); validate 2+ variable assignments per step and mixed Web/Windows navigation.

## Editor workflow
- Guide configuration: add/rename/remove variable, optionally set default value.
- Step configuration: add ordered action, select variable, choose set/read/clear/copy and timing; read action uses the same existing target picker.
- In a step target selector, a bound locator value can be chosen from the guide variable list. The Instructor stores a typed reference or canonical variable token, never a manually typed step key.
- Show missing/ambiguous variable errors before saving and at runtime; never silently guess a target.

## Release gate
The first Core types/schema additions are **definitions only**. Do not run the schema against an existing V5 database and do not claim the new variables are operational until V5 migration, Instructor, compiler and both runtime adapters are updated and validated.
