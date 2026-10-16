# GuideMe Instructor — V5 Authoring Design

**Status:** Proposed Instructor functionality; the production Learner already reads the V5 model. This document defines authoring requirements, not an implemented editor.

## Product contract

The author chooses the target application context, user action, UI element, expected result, validation evidence, advancement policy, and optional captured values. The Instructor must inspect externally observable application behavior; access to target source code, private databases, or runtime AI is not required. AI may assist during authoring but must not be necessary for Learner execution.

## Authoring flow

1. Choose Web or Windows application context and verify unambiguous discovery.
2. Choose an action (for example click, edit, select, wait, navigation, or information-only). Unsupported interaction kinds must not be published.
3. Identify a live UI element and generate locator candidates. Validate uniqueness; add anchors or frame context as needed.
4. Describe the expected outcome and select executable primary validation and any additional completion conditions. An observed click alone must not be presented as business success.
5. Choose a `Manual` or `AutomaticOnValidation` step advancement policy; automatic steps require valid, observable evidence.
6. Configure optional captures and references, validate runtime support, preview the complete step, and obtain author confirmation.
7. Save atomically to V5, then test execution on the actual target application.

## V5 persistence mapping

| Authoring concept | V5 entities |
| --- | --- |
| Guide and ordered steps | `Guides`, `GuideSteps` |
| Application instances and discovery | `GuideApplicationContexts`, `ApplicationContextMatchers` |
| Reusable UI elements, locators and anchors | `GuideElements`, `ElementLocators`, `ElementAnchorRelations` |
| Step-to-element roles | `StepElementUsages` |
| Instruction bubbles | `StepBubbles` |
| Required user interaction | `StepInteractions` |
| Observed primary/additional validation | `StepObservedSignals` |
| Intended outcome | `StepExpectedOutcomes` |
| Advancement policy | `StepAdvancePolicies` |
| Variables and captures | `GuideVariables`, `StepCaptures` |

The physical contract is `docs/schema/GuideMe-V5.sql`. Composite keys express ownership; `StepOrder` controls presentation order independently of immutable `StepId`. Use parameterized writes within transactions and enforce foreign keys. Do not revive retired repository classes or maintain a parallel persistence schema.

## Discovery and safety rules

- Use DOM and frame observations for Web; UI Automation for Windows.
- A target matching zero or multiple elements is not accepted without additional author intervention.
- The Instructor distinguishes a technical locator match, an interaction event, and evidence of successful completion.
- Observe UI changes after an action and suggest candidate conditions, but require explicit author confirmation for the semantic meaning.
- Never silently substitute unsupported validation types, arbitrary Boolean expressions, or customer-specific assumptions.
- Preserve the independence of the published Learner from the Instructor and from AI.

## Implementation boundary

The Instructor V5 writer, its editing UI, drafts, and AI-assisted proposals are not yet implemented. Existing V5 read/compile behavior is not a substitute for a transactional editor. Runtime capabilities must be confirmed against both adapters before enabling an authoring option.

## Acceptance criteria

1. A new Guide and its full hierarchy can be authored and edited without editing SQL or JSON manually.
2. Save, reorder, edit, and delete operations retain stable IDs and preserve referential integrity.
3. Ambiguous targets and unsupported validation rules block publication with actionable feedback.
4. Every published step is compilable by `GuideV5ExecutionCompiler` and executable without the Instructor.
5. Test Web, Windows and Web–Windows–Web guides through the production Learner, including negative cases and dynamic UI changes.
