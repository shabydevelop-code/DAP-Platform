# Architectural Decisions

This file contains the current accepted architectural rules for DAP Platform. Historical implementation details remain available through Git history; this document is intentionally limited to active decisions.

## ADR-001 — One desktop application

**Status:** Accepted

DAP is one Windows desktop application containing Learner and Editor modes rather than separate product executables.

## ADR-002 — Desktop technology

**Status:** Accepted

Use .NET 8 and WPF for the production desktop application.

## ADR-003 — Single Web browser-access architecture

**Status:** Accepted

Production Web learner execution and Web E2E use the same browser-access boundary:

```text
.NET Runtime / E2E runner
    ↕
Native Host
    ↕
Browser Extension
    ↕
Browser DOM
```

There is no alternate browser-control stack for Web execution or Web tests. Direct debugging-protocol control, Selenium, Puppeteer, or any test-only DOM-control path that bypasses the DAP extension boundary is prohibited.

Chrome and Edge are supported browser products, not separate DAP modes.

## ADR-004 — Windows Runtime

**Status:** Accepted

Use Microsoft UI Automation for native Windows target discovery, geometry, and interaction observation.

## ADR-005 — Shared Guide model

**Status:** Accepted

Web-only, Windows-only, and hybrid guides use the same Guide/Step/Validation/Progress domain model. Each Step declares its runtime.

## ADR-006 — Database independence

**Status:** Accepted

Core/domain logic must not depend on a database engine. SQLite is the first provider; additional providers may be introduced later.

## ADR-007 — Persistence ownership

**Status:** Accepted

**Seed initializes. DB owns. Runtime consumes.**

Seed/factory definitions initialize or explicitly reset known Guides. Normal execution loads and runs the persisted Guide and never silently overwrites it from seed code.

## ADR-008 — Numeric persistence IDs with stable keys

**Status:** Accepted

Persistence uses numeric internal IDs and separate stable textual keys. Database row IDs are internal and must not become user-facing runtime-routing identifiers.

## ADR-009 — Localization

**Status:** Accepted

DAP-owned UI supports Hebrew and English with RTL/LTR through external JSON localization files. Missing configuration or keys are explicit errors; there are no compiled translation fallbacks.

Guide instructional content remains Guide data rather than product-localization data.

## ADR-010 — Production-first architecture

**Status:** Accepted

Production runtime capabilities must not depend on temporary harnesses, development-only infrastructure, or reference projects.

## ADR-011 — Closed-target / black-box rule

**Status:** Accepted

DAP must support closed third-party targets. Production Runtime and Editor behavior must rely only on production-observable interfaces.

TestCRM source may be inspected during development for diagnosis and learning, but source knowledge must never become a runtime target, validation, or navigation oracle.

## ADR-012 — Guide navigation is context-aware

**Status:** Accepted

Step order is not application navigation. Automatic Steps advance only after their persisted validation/completion semantics succeed. Informational Steps may expose explicit learner confirmation.

Previous is not a universal `StepOrder - 1` operation and may be exposed only when the runtime can determine that the prior Step is safely renderable in the current application context.

## ADR-013 — Server-backed Web refresh preserves logical context

**Status:** Accepted

A server round trip may replace/rebuild the DOM without changing logical business context. DOM replacement alone is neither a context change nor Step completion.

The Web Runtime re-evaluates current context and re-resolves the target after refresh/replacement.

## ADR-014 — Frame hierarchy is target context

**Status:** Accepted

Web target context includes iframe hierarchy. Frame and target identities are transient and must be re-resolved after replacement.

A browser browsing-context name is not guaranteed to be stable identity after iframe replacement/promotion.

## ADR-015 — Target resolution never guesses

**Status:** Accepted

A target may use a primary locator plus multiple anchors/context constraints. Resolution returns exactly one target, NotFound, or Ambiguous. Multiple candidates must never be resolved by silently selecting the first.

## ADR-016 — Commit-based value validation

**Status:** Accepted

Text/value validation is based on a committed learner action rather than an intermediate valid value.

Web text commits on blur after a real edit. Windows text commits after a real edit followed by focus loss. Discrete controls commit on their natural change action.

Invalid non-click commits are consumed and require a new commit attempt.

## ADR-017 — Navigation-capable click durability

**Status:** Accepted

A validating click capable of browser-default navigation/submission must reach the DAP Runtime before the source document can be destroyed by that default action. Application click handlers remain part of the original action.

## ADR-018 — Bubble presentation may be promoted

**Status:** Accepted

Target ownership and bubble presentation surface are separate. Presentation may be promoted to the top-level page when a child frame cannot physically display the bubble. Target identity and validation remain owned by the original target/frame.

## ADR-019 — Explicit bubble drag handle

**Status:** Accepted

Draggable learner bubbles use a visible explicit handle. Only that handle begins dragging. Manual placement becomes authoritative for the active Step and reconciliation must not overwrite it.

## ADR-020 — Target viewport visibility controls attached bubble visibility

**Status:** Accepted

If an attached target leaves the visible viewport, its bubble is hidden. If the target returns while the Step remains active, presentation may resume without changing Guide state.

## ADR-021 — Explicit Guide completion

**Status:** Accepted

The learner receives an explicit completion surface/action after the final Step. Finishing the Guide does not imply closing the target business application.

## ADR-022 — TestCRM models server-backed enterprise behavior

**Status:** Accepted

DAP.TestCRM is a permanent representative black-box target. Actions that logically require a server round trip should reconstruct/refresh the relevant Web content while preserving logical context and applicable transient working state.

TestCRM must not be changed merely to make DAP targeting or testing easier.

## ADR-023 — E2E is a synthetic learner, not a second Guide engine

**Status:** Accepted

The automated runner may execute user actions, orchestrate infrastructure, and assert outcomes. It must not add hidden target selectors, validation rules, completion rules, or Step progression semantics that should already be provided by the persisted Guide and production Runtime.

A Guide that succeeds manually but requires E2E-only semantic help is evidence of an E2E architecture defect.

## ADR-024 — E2E uses real application readiness

**Status:** Accepted

Synchronization uses actual application signals: server responses, route state, frame/document identity, DOM state, validation state, and equivalent production-observable readiness.

Fixed delays are not substitutes for readiness.

## ADR-025 — Fast and Visual share one action path

**Status:** Accepted

`DAP_E2E_MODE=fast` and `DAP_E2E_MODE=visual` execute the same business/learner logic. Visual may add cursor travel and presentation pacing only; it must not change values, commit semantics, target selection, validation, or progression.

## ADR-026 — Canonical Web run modes

**Status:** Accepted

Supported public Web modes are:

```text
--manual
--guided
--manual-from-step N
--unguided
--visual-from-step N
```

Focused from-Step modes execute the real preceding workflow as bootstrap and then hand off/start DAP at the requested Step with validated resume context.

## ADR-027 — Browser profile auto-discovery

**Status:** Accepted

The extension-native Web runner and autonomous launcher discover the installed Chrome/Edge profile containing the registered DAP extension.

Zero matches fail explicitly. Multiple matches fail as ambiguous. DAP does not guess.

The historical browser-selector environment variable is not part of the active execution model.

## ADR-028 — E2E timeout ceiling

**Status:** Accepted

The default maximum E2E synchronization timeout is 5 seconds.

Any increase above 5 seconds, including temporary diagnostic changes, requires explicit user approval before implementation.

## ADR-029 — Process/output isolation

**Status:** Accepted

Canonical E2E runners build owned long-lived processes into unique per-run temporary output directories under `%TEMP%\DAP\E2E`.

The runner cleans up only processes it owns and must not kill an arbitrary process merely because that process occupies a required port.

## ADR-030 — Web stable/recovery reconciliation cadence

**Status:** Accepted

Current accepted Web learner cadence:

- recovery states: 100 ms;
- stable resolved Step: 500 ms.

This preserves the verified bubble semantics while reducing idle browser load. A previous fully event-driven stable-loop experiment caused visible learner regressions and is not accepted.

## ADR-031 — Manual learner is the product acceptance reference

**Status:** Accepted

The production Web learner must complete the persisted Guide without the E2E runner connected.

The verified runner-free 54-Step manual TestCRM run proves that persisted Guide data plus production Runtime logic are sufficient for target resolution, validation, completion, capture, and progression.

## ADR-032 — Documentation is part of project state

**Status:** Accepted

Architecture, decisions, current status, and project context are maintained in repository Markdown and updated with significant implementation changes.

## Windows E2E fidelity and asynchronous completion — 2026-10-06

Windows E2E follows the same semantic ownership rule as Web E2E: automation may perform learner actions and wait for the Runtime's externally visible Step, but it must not determine application business outcomes that belong to persisted Guide completion semantics.

For asynchronous Windows actions, completion must be expressed through a production-observable condition in the Guide. The canonical Case status-sort Step therefore waits for replacement of the observable `CasesGrid` after the server-backed sort rather than using a runner delay, HTTP knowledge, or E2E-side outcome polling.

The Windows Learner Runtime consumes persisted `AutoFocusTarget` as a one-time presentation behavior for value-entry Steps. Reconciliation must not repeatedly steal learner focus.

The full Windows Guided 54-Step workflow and a separate Manual autofocus check passed with these rules on 2026-10-06.

