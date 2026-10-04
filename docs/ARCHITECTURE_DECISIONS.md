# Architecture Decisions

## Runtime and persistence boundary

The DAP Runtime must remain independent of the physical database provider. SQLite is the current persistence implementation, not a runtime contract.

The domain/Core model is defined before committing to a physical database schema. Persistence adapters map the runtime-neutral model to SQLite or a future database provider.

## TargetDescriptor

A guide step references a runtime-neutral TargetDescriptor. It contains:
- Runtime: Web or Windows
- Frame/context information where applicable
- A primary locator
- Zero or more anchors/context constraints

Target resolution is candidate discovery followed by anchor matching and uniqueness verification. Ambiguous or missing targets are explicit resolution results; the runtime must not guess.

The physical storage representation of TargetDescriptor, including whether anchors are normalized tables or serialized JSON, remains an implementation decision until the Core model is stable.

## Implementation status and next architectural phase

The original implementation order through the Learner runtimes is now complete:

1. Core/domain contracts and models — implemented.
2. Persistence interfaces — implemented.
3. SQLite persistence adapter — implemented.
4. Learner Runtime and bubble lifecycle — implemented.
5. Web Runtime integration — implemented and exercised by the canonical 53-Step Guide.
6. Windows Runtime integration — implemented and exercised by the canonical 53-Step Guide.
7. Instructor/Editor Runtime and target-capture workflow — next major product phase.

Future work must build the Instructor/Editor path against the same stable Core, persistence, and runtime-neutral target contracts rather than introducing a parallel guide model.

## Centered informational Step presentation

A Guide Step may intentionally contain no target when its purpose is to present learner information rather than guide an interaction. The shared representation is `Target = null`, `BubblePlacement.Center`, and `StepAdvanceMode.Manual`.

This is a runtime-neutral Guide capability. Web and Windows use their native presentation adapters but preserve the same semantics: centered placement, no target pointer/highlight, localized confirmation action, and explicit learner dismissal before advancing.

Guide completion uses the same centered presentation family with completion-specific content and Finish action. This avoids a separate completion visual system and keeps Web/Windows learner presentation aligned.
