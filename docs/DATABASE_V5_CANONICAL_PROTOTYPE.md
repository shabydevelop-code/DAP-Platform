# GuideMe V5 — unified experimental schema, replacement target

**Status:** A new canonical *experimental* database has been built and validated separately. **Production replacement is not implemented**: neither the active `GuideMe.db` nor the Learner repository/reader has been switched to V5.

## V5 changes compared to V4

- Eliminated legacy `GuideSteps.AdvanceMode`; `StepAdvancePolicies` is the single recorded advancement policy.
- Eliminated legacy `StepValidations` and `StepCompletionConditions`; `StepObservedSignals` holds both primary validation and additional completion observations, distinguished by `SignalRole`, and retaining `DefinitionSource`.
- Removed the earlier `ReadableSteps` view; `ReadableStepSemantics` remains the canonical inspection view.
- Retained stable hierarchical keys (`GuideId,StepId`) and independent `StepOrder`; component definitions and use associations remain distinct.
- Retained `Unspecified` for interaction/outcome when the old source provides no verified meaning. This is incomplete authoring data, not a default action.

## Observed test results

A separate SQLite V5 prototype was built from the previous V4 experiment. It has **15 tables**, **3 guides**, **116 steps**, and **203 observed signals** (112 primary + 91 additional). `PRAGMA integrity_check` returned `ok`, `PRAGMA foreign_key_check` found no violations, and `ReadableStepSemantics` returned 116 rows. Counts were compared table by table with the V4 source for all retained tables.

The generated files are conversation artifacts, not yet tracked in GitHub:

- `GuideMe-V5-Prototype.db`
- `guideme-design/GuideMe-V5-schema.sql`
- `guideme-design/build_v5.py`

## Why the new schema is not yet the production mechanism

The production repository `SqliteGuideStepRepository` and the domain `GuideStep` still expect the old schema. Merely replacing the file would break production. Moreover, runtime evaluation requires preserving the current semantics of primary validation **AND** all additional completion conditions, the text focus-and-leave prerequisite, click events distinct from target disappearance, and baseline handling for target replacement.

**The intended target is full replacement, not permanent V4+legacy duplication.** The next implementation phase must provide one V5 domain reader/compiler and feed it into the Web/Windows adapters, with comprehensive regression on the 55-step Web guide, 55-step Windows guide, and six-step mixed guide, before explicitly switching the runtime DB. No compatibility layer is to be left indefinitely in production.

## Acceptance criteria before cutover

1. One authoritative definition of each step behavior in V5; no simultaneous legacy writes.
2. `GuideStep` runtime representation receives observations/advancement faithfully, whether directly or via one deterministic compilation pass.
3. An editable, understandable database structure: required interaction, element, expected outcome, observed checks and advancement.
4. Round-trip equality for all 116 existing steps and reliable references/captures after reorder.
5. Full Web, Windows, mixed-run regression and explicit release approval.
6. Preserve the active customer DB until a verified cutover; never overwrite it automatically.

## Explicit caveat

V5 consolidates duplicate structures but does **not** magically supply the missing human-authored interactions and outcomes. The Instructor must eventually capture those facts; existing guides may need explicit review. A DB cannot be claimed to explain a previously unspecified action merely because it has a dedicated column.
