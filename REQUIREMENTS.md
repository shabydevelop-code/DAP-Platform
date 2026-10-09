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

The production learner accepts `DAP.exe --guide <GuideId> --mode manual` (or omits `--mode`, defaulting to Manual). The product also parses `--mode hybrid`, but explicitly rejects it as unsupported before starting a Guide: persisted `AutomationValue` actions are currently executed by TestCRM-specific E2E drivers, not by the production learner. Hybrid is **not** a completed product feature. The required design is a runtime-owned action executor for both Web and Windows, with explicit persisted automation authorization, unique target resolution, learner-owned validation/progression, and no dependency on TestCRM, test-driver protocol or target application source. One Guide must support both modes. E2E runners remain test harnesses only. Instructor launch is not decided.
