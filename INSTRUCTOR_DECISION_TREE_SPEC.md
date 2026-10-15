# GuideMe Instructor — Universal Decision-Tree Generator Specification

Status: DESIGN PROPOSAL ONLY. No Instructor implementation or Learner behavior change.
Baseline: commit `5b2786016e6741045309e9b78a3afaf0c99e5ed5`.
Scope: generic Web and Windows step authoring, offline, no AI or application source-code dependency.

## 1. Product contract

An author selects an action, points at a target in the live application, defines what successful completion means, and chooses advancement. The Instructor discovers technical candidates, verifies them, and persists a `GuideStep` compatible with the existing model. It MUST NOT silently infer business intent from a click or assume an action succeeded merely because an event fired.

Four independent concepts:
1. **Target**: `TargetDescriptor` = runtime, locator, optional anchors and frame context.
2. **Action/observation**: click, value edit, selection, keyboard, navigation, waiting, or information-only.
3. **Completion**: primary validation AND any additional completion conditions.
4. **Advance**: automatic after completion or manual; never conflate an observed event with business success.

Author-facing questions use business language. Technical strategies, UIA trees, DOM selectors, regular expressions, and capture tokens are generated or shown only in an optional expert diagnostics view.

## 2. Declarative decision-tree engine

The wizard is a graph of declarative question nodes, not a hard-coded CRM-specific series of screens.

```text
Start
 ├─ Select runtime: Web | Windows
 ├─ Select intent: click | enter-value | select | keyboard | pointer |
 │                 wait-for-state | navigation | information-only
 ├─ Pick live target (unless information-only)
 │    ├─ Discover available targets and capabilities
 │    ├─ Generate locator candidates and optional anchors
 │    ├─ Resolve unique target; ambiguity => ask author
 │    └─ Re-check after UI change / rerender
 ├─ Choose action details supported by the target
 ├─ Choose success evidence
 │    ├─ value equals / non-empty
 │    ├─ target exists / absent / enabled
 │    ├─ observed click or disappearance
 │    ├─ compound conditions (AND in existing model)
 │    └─ unsupported evidence => explicit unsupported status
 ├─ Choose advancement: automatic | manual
 ├─ Optionally capture a value for later steps
 ├─ Preview generated persisted step
 ├─ Validate with real target app, including failure and retry paths
 └─ Save only after author approval and supported-schema validation
```

Nodes have: `id`, `visibleWhen`, `optionsProvider`, `requiredWhen`, `produces`, `validationRule`, `next`. Every option has a stable machine ID, human label, supported runtime(s), and support state: `supported`, `requires-extension`, `research`. Incompatible combinations are disabled with an explanation rather than silently approximated.

## 3. Action catalog (candidate taxonomy, not claim of current support)

| Family | Targets | Author's action | Typical evidence | Notes |
|---|---|---|---|---|
| Click | button, link, tab, icon, grid row | click, double-click, context click | click + new target appears / disappears / enabled | Only click validation is confirmed in shared execution policy; variants require adapter verification |
| Value entry | text, multiline, numeric, date | enter or edit | value-equals, value-not-empty | Text edits require committed focus transition in shared policy |
| Selection | dropdown, radio, checkbox, list/grid selection | select/toggle | value or checked state, dependent UI | Checked/selected evidence requires capability review |
| Keyboard | focused control/window | Enter, Tab, Escape, shortcut | observed state change | Not assume keyboard-event validation is supported |
| Pointer | scroll, hover, drag/drop | gesture | target/state change | Research; do not expose as implemented |
| Wait | target, modal, content, page | no user action | target-exists, target-not-exists, target-enabled, value-equals | Depends on adapter observation and step policy |
| Navigation | tab, window, frame, URL | navigate | page context or target evidence | URL and window conditions require verification |
| Information | no target | read instruction | author/user acknowledgement | Centered information has strict model invariants |

Action families do NOT determine success evidence automatically. A save button and an open-dialog button may both receive click events but require different postconditions.

## 4. Target discovery and ambiguity

- Operate only on externally observable UI: DOM/frame context for Web; accessibility/UI Automation for Windows. No application source code or demo-specific hard-coded knowledge.
- Generate candidates from stable IDs, names, roles, labels, hierarchy, nearby landmarks, row/column context, and other adapter-supported observations.
- Evaluate uniqueness in the current context and stability after controlled rerender, focus change, or navigation. A locator matching 0 or >1 targets is not accepted.
- Prefer the simplest validated locator; add anchors only when needed. Existing Web guides may require zero anchors; do not impose anchors universally.
- Explicitly distinguish locator confidence from validation correctness. Unique locator does not prove business success.
- Do not silently guess when multiple candidates remain. Ask the author to select or perform another observation.
- Dynamic rows may require a captured identifier from a prior step; unresolved dependencies block publication.

## 5. Completion evidence and runtime compatibility

Confirmed in current core sources:
- `StepAdvanceMode.AutomaticOnValidation` and `Manual`.
- `ValidationDefinition` value checks: `value-equals`, `value-not-empty`.
- Shared execution policy recognizes primary `clicked` and `target-disappeared` behavior.
- Additional `StepCompletionCondition` kinds: `target-exists`, `target-not-exists`, `target-enabled`, `value-equals`.
- Additional conditions combine with primary validation using AND in `GuideStepExecutionPolicy.CanAdvance`.
- Text edit validation requires a committed focus transition; already-present values are not treated as an edit event by default.
- Centered information-only steps require Manual mode, no target/context/validation/capture/completion conditions.
- `StepCaptureDefinition` supports runtime, locator, property, optional pattern and timing; timing support must be checked in platform adapters before promising it.

NOT established merely by this core inspection: universal keyboard validation, arbitrary OR/NOT boolean expressions, HTTP-response/business transaction success, checkbox state validation, navigation URL completion, drag/drop, full capture substitution across all fields, arbitrary retry policies. These are proposed extensions or research items. The wizard must refuse unsupported combinations instead of saving unexecutable steps.

## 6. Wizard question schema (pseudocode)

```yaml
question:
  id: completion.evidence
  prompt: "What proves that the action succeeded?"
  visibleWhen: target.selected && action.selected
  optionsProvider: observedEvidenceCompatibleWith(runtime, target, action)
  options:
    - id: target-appeared
      mapsTo: completionCondition(target-exists)
      support: supported-if-adapter-resolves
    - id: target-disappeared
      mapsTo: primaryValidation(target-disappeared)
      support: supported-if-adapter-observes
    - id: value-equals
      mapsTo: primaryValidation(value-equals)
      support: supported
    - id: business-save-succeeded
      mapsTo: none
      support: research
  requiredWhen: advance.mode == automatic
  onAmbiguous: ask-author
```

A mapping is valid only if the selected runtime adapter can execute the resulting condition. `supported-if-adapter-...` is not a blanket guarantee.

## 7. Generation pipeline

```text
Author intent -> observed UI -> candidate target descriptors
 -> target uniqueness/stability checks -> event/action choice
 -> observable success evidence -> core-model compatibility check
 -> dry-run against live application -> author approval
 -> persist GuideStep + anchors + capture + completion conditions
 -> replay on changed data -> publish
```

Preserve current database normalization (GuideSteps, TargetAnchors, StepCaptures, StepCompletionConditions). Do not migrate schema for a speculative wizard. New draft/authoring metadata, if required, should remain separate from published runtime definitions.

## 8. Acceptance criteria and regression

1. No AI, cloud access, target application source code, or TestCRM-specific logic required.
2. A nontechnical author can configure every supported combination without typing a selector, regex, UIA locator, or JSON.
3. Every published step has a uniquely resolved target, or is a valid information-only step.
4. The selected completion rule is observable and executable by the target runtime; ambiguous/unsupported cases block publication.
5. Event alone is not presented as proof of business success.
6. Failed validation does not advance. Successful completion advances only under the configured policy.
7. Capture dependencies are resolved and tested across different records and reordered grids.
8. The generated guide runs without AI and without the Instructor.
9. Existing proven Web and Windows guides and their runtime behavior remain unchanged.
10. Coverage audit: export the actual 55 Web and 55 Windows persisted steps, classify each by action, locator, context, validation, completion, capture and advancement, and map each feature to a catalog rule. Record uncovered cases and extend generic catalog rather than introducing app-specific branches.
11. Negative tests: missing target, duplicate target, stale target, wrong value, unsuccessful save, hidden iframe, dynamic grid reorder, application context switch, and manual information-only step.
12. Independent replay on changed test data; no claiming full coverage based solely on model inspection.

## 9. Evidence and limits

This proposal is grounded in:
- `src/DAP.Core/Guides/GuideStep.cs`
- `src/DAP.Core/Guides/GuideStepExecutionPolicy.cs`
- `src/DAP.Core/Guides/GuideValidationPolicy.cs`
- `src/DAP.Core/Guides/GuideCompletionPolicy.cs`
- `src/DAP.Core/Targets/{TargetDescriptor,Locator,Anchor}.cs`
- `CURRENT_STATUS.md`, `PROJECT_CONTEXT.md`

The local GuideMe SQLite database is not available to this GitHub-only inspection. Therefore coverage of the actual first 10 steps or all 110 Web/Windows steps has NOT been verified. Next deliverable is a read-only export/coverage report from `C:\ProgramData\GuideMe\Data\GuideMe.db` followed by a gap analysis. This document is a design spec, not implemented functionality.
