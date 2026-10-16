# GuideMe V3 — Physical Data Model Prototype

Status: **experimental, not production-ready**. No changes to active GuideMe database or Learner.

## Central design goal

A person opening the DB should be able to understand the *configured user action*, its application context, involved UI element, expected outcome and completion check. A generic Instructor and deterministic Learner must work from the same structured definitions. A readable sentence alone does not constitute an executable contract.

## Prototype (built separately from GuideMe.db)

The local `GuideMe-V3-Prototype.db` was built using `GuideMe-V3-schema.sql` and `build_v3.py` from the earlier V2 experimental DB, itself based on `docs/sample-data/GuideMe-Test.db`.

Counts: 3 guides, 116 steps, 116 StepInteractions, 226 GuideElements, 226 StepElementUsages, 226 locators, 21 anchor relationships, 91 completion conditions, 2 captures. SQLite integrity check: `ok`. FK violations: `0`.

**Caution**: 226 elements are intentionally imported as separate element definitions, not automatically deduplicated. Similar locators do not prove semantic identity. Sharing between steps is now *possible in the schema*, not established or verified in migrated data.

## Tables and ownership

- `Guides(GuideId)`, `GuideSteps(GuideId,StepId)`: stable hierarchical identity; `StepOrder` independent.
- `GuideApplicationContexts(GuideId,ContextId)`, `ApplicationContextMatchers(...MatcherId)`: application context and matching.
- `GuideElements(GuideId,ElementId)`: shared UI element definition; `Runtime`, `FrameContextJson`, `DisplayName`, `DefinitionStatus`.
- `ElementLocators(GuideId,ElementId,LocatorId)`: how an element is found.
- `ElementAnchorRelations(GuideId,ElementId,RelationId)`: relationship to another defined element; `AnchorOrder` independent.
- `StepElementUsages(GuideId,StepId,UsageId)`: why a step uses an element (primary, anchor, completion, capture); multiple usages possible.
- `StepInteractions(GuideId,StepId)`: explicit expected interaction, source confidence (`IsAuthorConfirmed`) and explanation.
- `StepValidations`, `StepCompletionConditions`: current distinction preserved pending behavioral analysis.
- `GuideVariables`, `StepCaptures`: variables and capture sources.
- `StepBubbles`: presentation/instruction.
- `ReadableSteps`: SQL VIEW joining guide, step, action status, instruction and summarized element roles/checks for inspection.

## Avoid inventing behavior

Existing `GuideStep` lacks a universal explicit interaction kind. Every migrated `StepInteractions.InteractionKind` is therefore `Unspecified` with `IsAuthorConfirmed=0`. The original guide instruction and low-level execution rules are preserved separately. Do **not** silently interpret an existing locator as proof that a click is required.

## Important limitations and next acceptance gates

1. The physical SQL and prototype are **experimental**. No Learner adapter exists.
2. Source `FrameContextJson` and validation `OptionsJson` remain opaque and require structured treatment where editable/meaningful.
3. Explicit machine-interpretable action types need validated authoring rules; until then `Unspecified` is truthful.
4. Expected outcomes, completion-rule combination semantics and advance policies need a complete execution-code audit before any consolidation.
5. Confirm which elements can genuinely be shared across steps. Scope may require version and/or application context.
6. Prove **semantic round-trip** of all 116 steps, not just row counts and FK validity.
7. Verify reorder, CRUD, dependencies and Web/Windows/Mixed behavior before adoption.
8. `SourceStepId` in imported element definitions records provenance only; it is not part of the element identity.

Artifacts generated in conversation, **not committed as binaries or SQL to GitHub**: `GuideMe-V3-Prototype.db`, `GuideMe-V3-schema.sql`, `build_v3.py`. To keep this repo accurate, don't claim those assets are tracked until explicitly uploaded.

## Decision

Continue developing in a separate experimental DB. Do not replace `docs/sample-data/GuideMe-Test.db` or the runtime DB. Prefer truthfully incomplete structured definitions over fabricated actions.
