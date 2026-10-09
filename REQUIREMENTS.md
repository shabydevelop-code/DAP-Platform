# Requirements

## Product

- DAP is a production-target .NET 8 Windows application.
- One product supports Learner and future Instructor/Editor experiences.
- Learner launch uses exactly `DAP.exe --guide <GuideId>` as the required product entry contract. No `--learner` mode argument or compatibility alias is accepted. The runtime type is derived from the persisted Guide. Instructor/Editor launch remains undecided.
- The current launcher supports Guides whose enabled targets belong to one runtime type; cross-runtime Guide execution is a future capability.
- Hebrew and English UI are supported with RTL/LTR direction.
- Guide content language is independent of product UI language.

## Guide model

- Persisted Guide data is the execution source of truth.
- Step order is distinct from application navigation.
- A Step may be automatic after validation or manual/informational.
- Targetless centered informational Steps are supported.
- Targets may use multiple anchors/context constraints.
- Missing or ambiguous targets must never be guessed.
- Disabled Steps retain their persisted order.
- Runtime owns validation, completion, capture required by the Guide, and advancement.

## Web

- Production Web execution uses the DAP browser Extension and Native Messaging host.
- Production Web execution must not depend on Playwright, CDP, Python, customer source code, customer database access, or private customer APIs.
- The Extension is an adapter; Guide sequencing/progression stays in .NET Runtime.
- Support dynamic DOM changes, frames, navigation, asynchronous server behavior, and target re-resolution.
- Synthetic E2E actions must remain test-only and must not become a second learner engine.
- Web Manual and Hybrid runners must identify their browser session through the Extension so intentional browser closure ends cleanly without treating the Chrome launcher PID as browser lifetime.

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

- TestCRM learner runners expose Manual and Hybrid only, and normal execution requires an explicit persisted Guide ID via `--guide <GuideId>`.
- The runner must pass that exact Guide ID to the product Learner; it must not choose a Guide implicitly.
- Hybrid may perform configured learner actions but Runtime owns outcomes and progression.
- Intentional target-application/browser closure must terminate Manual/Hybrid cleanly while genuine Runtime failures remain failures.
- Automated technical waits must not exceed five seconds without explicit approval.
- Human Manual/Hybrid response time is not an automated timeout.
- TestCRM source may aid diagnosis only; it must not become a production oracle.
- Independent Web and Windows testing must support starting TestCRM without starting DAP, then launching the production Learner separately against the already-open application. Completing the Guide must not terminate the target application or browser.

## Deployment

- Target Windows with .NET 8 Desktop Runtime.
- Web deployment must install/validate the browser Extension and Native Messaging host.
- Python and Playwright are not production prerequisites.

## Product execution modes

The same persisted Guide must run with `DAP.exe --guide <GuideId> --mode manual|hybrid` (Manual default). Windows Hybrid uses the production learner and persisted AutomationValue, with UIA target resolution, writable ValuePattern checks, verified Edit commit via TAB, and runtime-owned validation and advancement. Steps without automation values remain manual. No TestCRM E2E driver or target application source may be required. Existing SQLite columns support the configuration. Web Hybrid product execution remains a required capability and is not yet implemented.

## Shared guide execution policy

`DAP.Core.Guides.GuideRunPlan` is the common runtime-neutral owner of ordered Guide Steps, start-step selection, disabled-step lookahead, captured values, runtime capture-token substitution, and asynchronous step lifecycle orchestration. Both the Web adapter guide runtime and Windows guide runtime use it. Target resolution, active-step validation, bubble presentation, and UI-specific actions remain in their respective runtimes/adapters; a single unified active-step validation/presentation engine has **not** yet been implemented. Session termination and build-lock preflight are handled by the shared DAP.App host. Web tab/window activation remains an open gap; production Web Hybrid value automation is implemented but unverified. This is current architecture, not a completed full runtime unification.

## Shared value-validation policy

`DAP.Core.Guides.GuideValidationPolicy` now evaluates persisted `value-equals` and `value-not-empty` rules using observed values supplied by runtime adapters. `WindowsValidationEvaluator` delegates these checks to Core while retaining Windows UI Automation `ValuePattern` access. The Web browser adapter also delegates value checks to Core, preserving its existing missing-expected-value behavior. Web event/commit handling, completion conditions, target resolution, and bubble presentation are unchanged. This is a focused first step, not a completed unified active-step validation engine. A fresh build and Web/Windows regression are still required.

### Shared completion-condition policy

`DAP.Core.Guides.GuideCompletionPolicy` evaluates `target-exists`, `target-not-exists`, `target-enabled`, and `value-equals` from runtime observations. Both Web and Windows now delegate these four checks to Core. Target inspection/resolution remains adapter-specific; Windows `target-replaced` still compares UIA element identities locally. Web retains its previous treatment of unresolved targets for `target-not-exists`. Build and regression verification of this change are pending.

### Production Web Hybrid implementation (pending regression)

The production Web learner now accepts `DAP.exe --guide <GuideId> --mode hybrid`. After target resolution and presentation readiness, it applies persisted `AutomationValue` once through the extension to a uniquely resolved writable text input/textarea, using focus, native value setter, input/change events and blur. The normal validation/commit and completion-condition pipeline still controls advancement. Steps without `AutomationValue` remain user-operated. The extension command is production-scoped and does not invoke TestCRM test-driver operations. No claim of end-to-end PASS is made until a local build and full Web/Windows Manual/Hybrid regressions are reported. Non-text value automation and browser tab activation are not implemented.

Standalone TestCRM Web Host opens the application in Chrome automatically after the five-second readiness check. DAP.exe remains a separate product process and does not own the target application's browser or servers. This host change requires a local Windows execution check.

The standalone TestCRM Web Host treats user-requested Ctrl+C termination as normal shutdown, stops its owned Web and backend processes, and does not report their exit code 0 as an unexpected failure. Unexpected independent process exits remain errors. Requires local verification.

Current implementation: both Web and Windows use the Core GuideExecutionEngine for guide sequence orchestration. Active step execution remains platform-specific; complete engine consolidation and Windows regression verification are outstanding.

Core GuideStepExecutionPolicy now enforces centered information-step invariants for both Web and Windows, and Web hybrid value-step eligibility. Active-step loops remain platform-specific. Build and E2E regression are pending; do not mark engine consolidation complete.

Both Web and Windows Hybrid value entry now apply the same Core GuideStepExecutionPolicy eligibility check. Platform-specific value assignment and active-step execution remain in the adapters. Full engine unification is not yet complete; build and regression verification pending.
