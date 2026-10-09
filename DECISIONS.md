# Architecture Decisions

This file contains the architectural decisions that are active for the current codebase. Retired implementation history is intentionally left to Git history rather than mixed into current-state documentation.

## ADR-001 — Runtime and persistence are provider-independent

Core and Runtime depend on persistence abstractions, not SQLite. SQLite is the current provider and may be replaced or supplemented without changing Guide semantics.

## ADR-002 — Target resolution is deterministic

A Step uses a runtime-specific target descriptor behind the shared Guide model. Multiple anchors/context constraints are supported. Missing or ambiguous targets are explicit resolution failures; Runtime must not guess.

## ADR-003 — Closed-application compatibility is mandatory

Production DAP must operate without target application source code, internal database access, or private APIs. TestCRM source may be used for development diagnosis only.

## ADR-004 — Runtime owns learner semantics

Persisted Guide data and production Runtime own target resolution, validation/completion observation, capture/materialization required by the Guide, and Step advancement.

Test/Hybrid action drivers perform learner input only. They must not duplicate outcome detection or become a second Guide engine.

## ADR-005 — Web production uses Extension + Native Messaging

Production Web execution uses the DAP browser Extension, Native Messaging host, named-pipe adapter, and .NET Web Runtime.

Playwright and CDP are not production dependencies.

The Extension is a browser adapter; Guide sequencing and progression remain in .NET Runtime.

## ADR-006 — Windows production uses UI Automation

Windows learner target discovery, observation, validation signals, and bubble positioning are implemented through Microsoft UI Automation and the production Windows Runtime.

## ADR-007 — Guide summary is persisted

The canonical Guide summary is Step 55 on Web and Windows. It is a targetless centered Manual Step. Runtime must not synthesize a second completion Step after it.

## ADR-008 — Disabled Steps preserve identity

A disabled persisted Step is skipped without renumbering. Disabling a Step must not bypass business state required by later Steps.

## ADR-009 — Canonical learner runners expose Manual and Hybrid only

Current TestCRM learner execution modes are `--manual` and `--hybrid`, and normal execution requires `--guide <GuideId>`. The runner must load and pass the explicitly supplied Guide ID to `DAP.exe --guide <GuideId>` rather than selecting a Guide implicitly.

`--reset-guide` is maintenance. `--published-dap` may be used as a packaging/path option.

Guided, Unguided, Fast, Visual, Manual-From-Step, Visual-From-Step, and `DAP_E2E_MODE` are retired.

## ADR-010 — Hybrid automation is persisted-data-driven

Hybrid may apply an explicit persisted `AutomationValue` to the persisted target. It must not invent hidden actions, validations, completion rules, or target-detection logic.

## ADR-011 — Human waits are not automated timeouts

The five-second timeout policy applies to automated technical waits. A human learner in Manual or Hybrid may remain on a Step longer than five seconds.

Automated E2E timeouts must not be increased beyond five seconds without explicit approval.

## ADR-012 — Current GitHub main is the code source of truth

Current `HEAD` of `main` is authoritative for implementation state.

History may be inspected only when explicitly requested for historical investigation or recovery. Documentation that conflicts with current code must be corrected rather than used to override current code.

## ADR-013 — AI is optional, never required

AI may assist development and may become an optional Instructor aid. Production Instructor and Learner must remain deterministic and functional without AI.

## ADR-014 — Instructor shares the Learner Guide model

Instructor/Editor must author the same persisted Guide model consumed by Learner. A parallel Instructor-only Guide model is not permitted.

## ADR-015 — Runner lifetime follows the target session

Intentional closure of the target application/session ends Manual and Hybrid cleanly; genuine Runtime failures remain failures. Windows uses target-process lifetime. Web uses a runner-owned Extension session identity because the Chrome launcher PID is not a reliable browser-lifetime signal. Manual Web uses that session only for passive lifetime observation and performs no synthetic learner actions.

## ADR-016 — Runtime initial input focus precedes Hybrid input

Production Runtime owns the one-time initial focus of input Steps. Hybrid must wait until that focus setup is complete before applying a configured learner input, so synthetic commit actions such as TAB cannot race a later Runtime focus operation.


## ADR-017 — Learner launch is Guide-driven

The product launch contract is `DAP.exe --guide <GuideId>`; no Learner mode argument or legacy mode alias is accepted. The persisted Guide determines whether the current execution uses Web or Windows Runtime; callers do not select a runtime mode. The current launcher requires all enabled targeted Steps in a Guide to use one runtime type. Cross-runtime execution can be added without changing the external Guide-selection contract.

## ADR-018 — Browser Extension has no product GUI

The browser Extension is an infrastructure adapter between the Web Runtime and browser. It does not own Guide selection, Guide management, settings, or other product UI, and therefore exposes no popup GUI.

## ADR-019 — Learner and target application have independent lifetimes

Production Learner attaches to an already-open application using the persisted Application Context. DAP must not require an E2E runner to open or own the target application. Guide completion ends the Learner without closing the target application or browser. Standalone TestCRM Web and Windows hosts support this execution path; integrated E2E runners remain available for regression.

## ADR-020 — Windows application identity is persisted and deterministic

Windows application identity is resolved from persisted named Application Context matchers, separately from Step target descriptors. The current resolver supports `WindowTitleContains`, `AutomationId`, and `ProcessName` and requires a unique top-level window match. Current execution supports one Windows context at startup; multi-context switching, delayed discovery, and rebinding remain required.

## ADR-021 — Unified production execution modes

The product uses `DAP.exe --guide <GuideId> --mode manual|hybrid` with Manual as default. One persisted Guide supports both modes. Windows Hybrid actions are executed by the production learner using resolved UIA targets and persisted AutomationValue. Only writable ValuePattern targets with automatic validation qualify. Edit values are committed by verified TAB focus traversal; the existing runtime owns completion and progression. TestCRM E2E automation is not a product dependency. The SQLite schema already supports the configuration. Web production Hybrid remains unsupported until the extension implements production actions.

## Shared guide execution policy

`DAP.Core.Guides.GuideRunPlan` is the common runtime-neutral owner of ordered Guide Steps, start-step selection, disabled-step lookahead, captured values, runtime capture-token substitution, and asynchronous step lifecycle orchestration. Both the Web adapter guide runtime and Windows guide runtime use it. Target resolution, active-step validation, bubble presentation, and UI-specific actions remain in their respective runtimes/adapters; a single unified active-step validation/presentation engine has **not** yet been implemented. Session termination and build-lock preflight are handled by the shared DAP.App host. Web tab/window activation remains an open gap; production Web Hybrid value automation is implemented but unverified. This is current architecture, not a completed full runtime unification.

## Shared value-validation policy

`DAP.Core.Guides.GuideValidationPolicy` now evaluates persisted `value-equals` and `value-not-empty` rules using observed values supplied by runtime adapters. `WindowsValidationEvaluator` delegates these checks to Core while retaining Windows UI Automation `ValuePattern` access. The Web browser adapter also delegates value checks to Core, preserving its existing missing-expected-value behavior. Web event/commit handling, completion conditions, target resolution, and bubble presentation are unchanged. This is a focused first step, not a completed unified active-step validation engine. A fresh build and Web/Windows regression are still required.

### Shared completion-condition policy

`DAP.Core.Guides.GuideCompletionPolicy` evaluates `target-exists`, `target-not-exists`, `target-enabled`, and `value-equals` from runtime observations. Both Web and Windows now delegate these four checks to Core. Target inspection/resolution remains adapter-specific; Windows `target-replaced` still compares UIA element identities locally. Web retains its previous treatment of unresolved targets for `target-not-exists`. Build and regression verification of this change are pending.

### Production Web Hybrid implementation (pending regression)

The production Web learner now accepts `DAP.exe --guide <GuideId> --mode hybrid`. After target resolution and presentation readiness, it applies persisted `AutomationValue` once through the extension to a uniquely resolved writable text input/textarea, using focus, native value setter, input/change events and blur. The normal validation/commit and completion-condition pipeline still controls advancement. Steps without `AutomationValue` remain user-operated. The extension command is production-scoped and does not invoke TestCRM test-driver operations. No claim of end-to-end PASS is made until a local build and full Web/Windows Manual/Hybrid regressions are reported. Non-text value automation and browser tab activation are not implemented.

Standalone TestCRM Web Host opens the application in Chrome automatically after the five-second readiness check. DAP.exe remains a separate product process and does not own the target application's browser or servers. This host change requires a local Windows execution check.
