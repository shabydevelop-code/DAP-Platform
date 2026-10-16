# GuideMe Instructor — V5 authoring contract (draft)

## Scope
Instructor is an independent authoring application. It records UI interactions against third-party Web and Windows applications without requiring source code for the target application. It must not use AI to create or run a guide. All authoring analysis and runtime execution are deterministic; AI integration is out of scope.

## Single sources of truth
- `Guides`: guide identity and display name.
- `GuideApplicationContexts` and `ApplicationContextMatchers`: application/runtime identification.
- `GuideSteps`: ordered, enabled steps and application context association.
- `GuideElements`, `ElementLocators`, `ElementAnchorRelations`: reusable target definitions.
- `StepElementUsages`: step-specific roles for reusable targets.
- `StepInteractions`: intended author-recorded action; never infer from bubble text.
- `StepExpectedOutcomes`: author-intended outcome; distinct from concrete observed signals.
- `StepObservedSignals`: machine-checkable validation and completion conditions.
- `StepAdvancePolicies`: progression behavior.
- `StepBubbles`: learner-facing instruction text and placement, independent of recorded action.
- `GuideVariables` and `StepCaptures`: data capture definitions.

## MVP decisions (approved)
- Web-first Instructor, separate Windows GUI; no branching, alternate paths or graph routing in MVP.
- The author explicitly starts and ends each step recording. No automatic segmentation.
- One linear successor per enabled step. A validation error can be the *expected* result of a step; its acknowledgement/correction is captured in subsequent linear steps.
- Workflow: Start step -> perform target-app action -> End step -> deterministic analysis -> review/correct proposal -> approve and save.
- Never silently infer uncertain completion conditions. Surface confidence/alternatives and require explicit author confirmation.
- Do not change the production MANUAL/HYBRID learner behavior for this MVP.

## Proposed recording sequence
1. Select/create a guide and associate its Web application context.
2. Explicitly start a recording window and capture the before-state.
3. Observe a user interaction and capture its target locator, runtime and optional frame/anchor context.
4. Explicitly end the recording window and capture the after-state; reuse an existing element definition if identity is confirmed; otherwise create a new element.
5. Create the step and link target usages to element definitions.
6. Store the interaction independently of the bubble text.
7. Compare resulting UI changes and let the author select or confirm expected outcomes and validation signals.
8. Configure advancement policy and optional variable captures.
9. Enter/edit the bubble content separately.
10. Validate referential integrity and compile a read-only execution plan before publishing.

## Non-goals and safeguards
- Do not parse instructional prose to infer actions.
- Do not inspect target application source code.
- Do not use AI in Instructor or learner runtime.
- Do not implement branching in the first Instructor version.
- Do not remove `StepInteractions` or `StepExpectedOutcomes` just because migrated guides currently contain `Unspecified` values.
- Do not replace the established MANUAL/HYBRID runtime modes or change production launch as part of the initial Instructor design.

## Gaps to resolve before implementing the editor
- Explicitly define supported interaction kinds, including no-action/wait steps.
- Define outcome semantics and author-confirmation lifecycle.
- Define element identity/reuse and multiple locator ordering/fallback behavior.
- Specify step creation/edit transactions, undo and validation errors.
- Verify whether V5 compiler is wired into the actual production launch; comments currently describe it as experimental.
- Normalize `GuideElements.Runtime` representations (`Web`/`Windows` vs numeric enum strings) only after confirming parser compatibility.
- Establish automated DB and compiler tests on all three sample guides without UI-specific hardcoding.

This document is a design contract, not a claim that Instructor functionality is already implemented.
