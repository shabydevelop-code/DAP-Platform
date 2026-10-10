# Requirements

## Product

- DAP is a production-target .NET 8 Windows application.
- One product supports Learner and future Instructor/Editor experiences.
- Learner launch uses exactly `DAP.exe --guide <GuideId>` as the required product entry contract. No `--learner` mode argument or compatibility alias is accepted. The runtime type is derived from the persisted Guide. Instructor/Editor launch remains undecided.
- The current launcher supports Guides whose enabled targets belong to one runtime type; cross-runtime Guide execution is a future capability.
- Hebrew and English UI are supported with RTL/LTR direction.
- Guide content language is independent of product UI language.
- Web and Windows learner bubbles provide a localized **End assistance** / **סיים ליווי** action. Stopping removes guidance, ends the learner process, preserves the target application, and permits launching the Guide again.

## Guide model

- Persisted Guide data is the execution source of truth.
- Step order is distinct from application navigation.
- A Step may be automatic after validation or manual/informational.
- Targetless centered informational Steps are supported.
- Targets may use multiple anchors/context constraints.
- Missing or ambiguous targets must never be guessed.
- Disabled Steps retain their persisted order.
- Runtime owns validation, completion, capture required by the Guide, and advancement.
- Web and Windows input Steps accept an unchanged pre-existing value after a focus-to-blur interaction, provided the persisted validation and completion conditions succeed. Presence of a value alone is insufficient.

## Web

- Production Web execution uses the DAP browser Extension and Native Messaging host.
- Production Web execution must not depend on Playwright, CDP, Python, customer source code, customer database access, or private customer APIs.
- The Extension is an adapter; Guide sequencing/progression stays in .NET Runtime.
- Support dynamic DOM changes, frames, navigation, asynchronous server behavior, and target re-resolution.
- Production browser extension and Native Messaging host must not contain synthetic E2E actions, test-driver commands, or a dedicated test-driver pipe. Browser closure must be handled by the production learner independently of any test runner.

## Windows

- Production Windows execution uses Microsoft UI Automation.
- Support target discovery/re-discovery, native learner bubbles, interaction observation, validation, and one-time Runtime-owned initial focus for input Steps.
- Production behavior must work against closed third-party Windows applications.
- Windows application discovery uses persisted named contexts and must reject ambiguous window matches; application discovery is separate from Step target discovery.
- Standalone Windows Learner execution must attach to an already-open target without an E2E runner and must not close the target on Guide completion.
- Multiple Windows contexts, delayed discovery, and rebinding remain required beyond the current single-context implementation.

## Data

- Core and Runtime remain independent of the concrete database provider.
- SQLite is the first provider.
- Additional providers must be possible without rewriting Guide/Runtime business logic.
- DAP data and target-application business data remain separate.

## Instructor/Editor

- Instructor/Editor authors the same persisted Guide model consumed by Learner.
- Production authoring must be capable of working against closed applications.
- AI may be optional assistance but is not a required production dependency.

## Testing

- Product verification must exercise the production Learner against already-open third-party-style Web and Windows applications, independently of demo or test runners.
- Demo applications and test harnesses are optional verification fixtures, not execution dependencies or business-rule authorities.
- Automated technical waits must not exceed five seconds without explicit approval; user interaction time is not a technical timeout.
- Verify ambiguous-target rejection, dynamic capture and reuse, navigation, validation, completion, and clean learner exit without closing the target application.

## Deployment

- Target Windows with .NET 8 Desktop Runtime.
- Web deployment must install/validate the browser Extension and Native Messaging host.
- Python and Playwright are not production prerequisites.

## Product execution modes

- The Learner launches with `DAP.exe --guide <GuideId>`; `--mode manual|hybrid` is optional, with Manual as default.
- Hybrid may apply only persisted, supported automation values; Runtime retains validation and advancement ownership. Steps without automation values remain manual.
- Platform-specific Hybrid capability and regression status belong in `CURRENT_STATUS.md`; do not treat demo-runner behavior as a production guarantee.

## Shared guide execution

- `GuideExecutionEngine` provides runtime-neutral sequencing, capture-token materialization, and Step dispatch through platform adapters.
- Capture definitions may read a UI-observed value in one Step and reference it in a later Step using `{{step:<step-id>:capture}}` within target or anchor locator values.
- Missing capture references fail explicitly rather than guessing a target.
- Capture timing persistence and broader variable substitution are not yet complete; see `ARCHITECTURE.md` and `CURRENT_STATUS.md`.
- Platform-specific UI observation and presentation remain separate. Shared Core contracts alone do not prove Web/Windows behavioral parity.
