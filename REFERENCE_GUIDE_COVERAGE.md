# Reference Guide: capability coverage and parity contract

Status: source-audited planning baseline, **not** a created or executed Guide. Do not modify the validated 55-step Web/Windows Guides. Create a new Guide only after checking the actual SampleApp UI targets and the database definitions.

## Purpose
Build one frozen reference Guide (or a coordinated Web/Windows pair if runtime transition constraints require it) using the existing SampleApp Web and Windows applications. Verify it end-to-end, then recreate an equivalent Guide with the future Instructor. Compare both persisted structure and observed runtime behavior. Do not simplify the reference to accommodate Instructor limitations.

## Code-derived capability matrix

| Capability | Web runtime | Windows runtime | Reference coverage / caution |
|---|---|---|---|
| Primary locator | `css`, `text`, `label`, `role` in extension content runtime | `automation-id`, `name`, `control-type`, `name-regex` in UIA resolver | At least one observable target per practical strategy; confirm actual UI supports it |
| No anchors | Supported | Supported | Baseline unique target |
| Multiple anchors | All anchors filter candidate | All anchors filter candidate | Test two simultaneous anchors |
| `Self` anchor | **Not handled** by current Web anchor switch | Supported | Windows only; Web needs runtime work before parity |
| `Context` / `Ancestor` | CSS-only | Supported | Both, with different semantics |
| `Descendant` | CSS-only | Supported | Both |
| `Sibling` | CSS-only | Supported | Both |
| `Nearby` | CSS-only | Supported | Both; semantics differ |
| `ColumnHeader` | **Not handled** by Web anchor switch | Special Windows resolver path | Windows only until Web support exists |
| Ambiguous/not found | Explicit statuses | Explicit statuses | Negative probes, never select arbitrary first match |
| Validation | Browser event/value checks in extension and adapter; audit each actual kind before authoring | `value-equals` and `value-not-empty` via UIA ValuePattern, plus runtime action handling | Use only verified combinations |
| Completion conditions | Shared policy: `target-exists`, `target-not-exists`, `target-enabled`, `value-equals` | Same shared policy, adapter-dependent observation | Verify in each runtime |
| Capture timing | `BeforeAction`, `DuringStep`, `AfterAction` model; explicit timing persisted | Same | Verify observation availability, not merely DB round trip |
| Legacy capture timing | `BeforeAction` | `DuringStep` or `AfterAction` | Already regression verified |
| Capture token substitution | Primary locator and anchor locator values | Same shared run plan | No claim of substitution in all Guide fields |
| Bubble placement | Auto/Top/Right/Bottom/Left/Center model | Same model | Test supported visual behavior |
| Instruction-only | Nullable target | Nullable target | Include at least one |
| Frame path | `FrameContext` model and browser implementation | Not applicable to Windows | Web iframe target |
| Application context | Persisted application matchers | Persisted application matchers | Verify switching only in supported cases |
| Mixed Web/Windows | Existing mixed smoke Guide reported validated | Existing mixed smoke Guide reported validated | Preserve a cross-runtime case; do not infer arbitrary transitions |

## Proposed reference scenario families

1. **Web locator/anchor probes:** uniquely identifiable control, duplicate controls disambiguated with CSS ancestor/context, descendant, sibling, nearby and multiple anchors. Include a deliberately ambiguous diagnostic case outside the successful learner path.
2. **Web dynamic workflow:** iframe transition, grid reorder or rerender, context change, validation/completion conditions, and capture from a real SampleApp value reused by a later target.
3. **Windows locator/anchor probes:** automation-id, name, control-type, name-regex; self, ancestor/context, descendant, sibling, nearby and column-header where the actual UIA tree permits. Include two anchors on a repeated row target.
4. **Windows dynamic workflow:** a grid/row operation, changed value, UIA re-resolution, completion condition and a captured value reused later.
5. **Shared presentation:** instruction-only, centered information, bubble placement, enabled/disabled steps and manual/automatic progression where supported.
6. **Cross-runtime:** reproduce a verified Web → Windows → Web transition using persisted application contexts, without modifying the existing smoke Guide.

## Required pre-creation audit

- Inspect the actual existing SampleApp Web/Windows UI elements and UIA/DOM trees. Do not fabricate selectors, AutomationIds, element names, or business records.
- Inspect existing persisted Guide definitions to reuse demonstrated application contexts, targets, and validation semantics, while creating **new** Guide IDs and steps.
- Verify actual Web validation-kind branches, Windows runtime event handling, capture property support, and the supported mixed-runtime launch path.
- Record for each proposed step: app, user action, target locator, all anchors and relations, frame/context, validation, completion condition, capture property/timing, expected transition, and evidence.
- Mark unsupported/unobservable combinations as gaps, not silently skip or pretend they passed.
- Freeze the verified reference Guide definitions (export/manifest with hashes), then author an Instructor-produced counterpart and compare normalized persisted definitions and runtime results.

## Acceptance criteria

- Both reference flows complete against the unmodified existing SampleApp applications.
- No ambiguous target is silently chosen; every successful step resolves its intended target.
- Capture and substitution values are verified against actual UI observations.
- No production behavior depends on SampleApp-specific source internals.
- Instructor recreation has equivalent target, anchors, validations, capture, context, sequencing and learner-visible behavior; differences are documented, not hidden.
- Existing Web/Windows 55-step Guides and the existing mixed smoke Guide remain unchanged.

## Evidence already available

- `src/DAP.Core/Targets/{Locator,Anchor,TargetDescriptor,FrameContext}.cs`
- `src/DAP.Core/Guides/{GuideStep,GuideRunPlan,GuideValidationPolicy,GuideCompletionPolicy}.cs`
- `src/DAP.Runtime.Windows/Targets/WindowsTargetResolver.cs`
- `src/DAP.Runtime.Windows/Validation/WindowsValidationEvaluator.cs`
- `src/DAP.Runtime.Web.Extension/content-runtime.js`
- `src/DAP.Data.Sqlite/Guides/SqliteGuideStepRepository.cs`

This is a coverage specification, not proof that every combination is executable. The next step is a concrete target inventory and per-step plan, followed by creation and execution of the new Guide.
