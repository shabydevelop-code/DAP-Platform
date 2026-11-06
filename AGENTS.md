# GuideMe — binding development rules

These rules apply to every change in this repository, including changes made by AI assistants.

## Mandatory cross-runtime contract
- Instructor is one product for Web and Windows. A shared feature must have the same authoring options in both runtimes unless an explicit, documented technical limitation has been approved.
- Runtime-specific adapters may differ internally. Do not hide a shared authoring option behind a Web/Windows condition to avoid implementing the other adapter.
- Before marking a shared feature complete, verify capture, edit, persistence, and Learner execution for **both** Web and Windows.
- If one runtime is unfinished, state that clearly; do not describe the feature as complete.
- Do not introduce legacy compatibility, extra controls, or fallback paths without a concrete requirement. Remove superseded code.
- Work on `main` only, as requested by the project owner.

## Mandatory verification
Run `python scripts/check-instructor-parity.py` before claiming a change to Instructor targeting is complete. A failure blocks a completion claim. Also run applicable build/tests; report tests that could not be run.

## Specific regression: "Any value"
The `Any value` matching mode must be offered at capture and edit time for supported nonempty-text targets on both Web and Windows. The Windows Learner must enforce nonempty value matching while preserving the target's structural identity. A Web-only implementation is incomplete.

These rules are not themselves proof of compliance: automated tests and runtime verification are required.
