# Project Context

## Source of truth

Primary repository:

```text
shabydevelop-code/DAP-Platform
```

DAP-Platform is the product source of truth. Reference projects may be inspected for ideas or diagnosis, but DAP architecture and implementation decisions must come from this repository.

## Product

DAP Platform is a production Digital Adoption Platform for creating and running interactive guides across Web and Windows applications.

The product is one Windows desktop application with two logical modes:

- Learner — runs and completes guides.
- Editor — creates, records, edits, previews, and manages guides.

Learner and Editor are separate runtime concerns even though they belong to the same product application.

## Runtime scope

Guides may be:

- Web only;
- Windows only;
- hybrid, moving between Web and Windows Steps.

Web and Windows share the same runtime-neutral Guide, Step, Target, Validation, Completion, Capture, and Progress concepts.

## Runtime technologies

Web:

```text
.NET Runtime
↕
ExtensionWebBrowserAdapter
↕
Native Host
↕
Browser Extension
↕
Browser DOM
```

Windows:

```text
.NET Runtime
↕
Microsoft UI Automation
↕
Native Windows application
```

Desktop GUI:

```text
.NET 8 + WPF
```

Python is not a production dependency.

## Single Web browser path

The browser extension architecture is the only active Web browser-access path for both production learner execution and Web E2E.

Web E2E may send explicit test-driver commands through the same extension boundary, but it may not create a second browser-control architecture.

Direct browser debugging-protocol control, Selenium, Puppeteer, or any test-only DOM-control path that bypasses the DAP extension boundary is not permitted.

Chrome and Edge are supported browser products. They are not separate DAP execution modes.

The installed browser profile containing the DAP extension is discovered automatically. No active browser-selector environment variable is required.

## Production ownership boundary

The .NET Runtime owns:

- active Step state;
- Guide sequencing;
- validation decisions;
- completion-condition policy;
- runtime capture/materialization;
- progression.

The extension owns browser mechanics only:

- tab/frame routing;
- target resolution from Runtime-supplied descriptors;
- DOM/browser event observation;
- content-script lifecycle;
- learner bubble presentation;
- browser fact/event reporting.

The E2E runner is a synthetic learner and infrastructure orchestrator only.

## Closed-target / black-box rule

DAP must work against closed third-party applications.

Production Runtime and Editor behavior must not require:

- target source code;
- target internal databases;
- private target APIs;
- privileged implementation details unavailable through production-observable interfaces.

TestCRM source may be inspected during development to diagnose behavior. That knowledge may help identify a generic product defect, but it may not become a runtime shortcut or target oracle.

## Target resolution model

A target is represented by `TargetDescriptor`, not by a single ad-hoc selector.

A descriptor may contain:

- runtime;
- primary locator;
- zero or more anchors/context constraints;
- frame context.

Resolution must be deterministic:

- exactly one candidate → resolved;
- zero candidates → NotFound;
- multiple candidates → Ambiguous.

DAP must never guess or silently select the first candidate.

## Frame/context rule

Web frame hierarchy is part of target context.

DOM nodes and browser-frame identities are transient across server round trips, reloads, rerenders, and iframe replacement.

The Runtime and extension re-resolve the live frame and target from persisted semantics.

TestCRM intentionally exercises iframe replacement/promotion. A historical browsing-context name must not be treated as stable identity.

## Validation rules

Text validation is commit-based rather than live-value based.

Web:

- real edit;
- blur;
- evaluate the committed value.

Windows:

- real edit observed through UIA;
- focus leaves the target;
- evaluate the committed value.

Discrete controls commit on natural change/selection actions.

Invalid non-click commits are consumed and require a new learner commit attempt.

Valid commits may remain latched while persisted post-action completion conditions are pending.

## Persisted Guide ownership

The persistent DAP database is the source of truth once a Guide has been initialized.

Rule:

**Seed initializes. DB owns. Runtime consumes.**

The normal database location is:

```text
C:\ProgramData\DAP\Data\DAP.db
```

`DAP_DATABASE_PATH` remains the supported override.

A seed/factory definition is initialization/reset data only. Normal execution must not silently replace an existing persisted Guide.

SQLite is the current provider, not a Core dependency.

## Runtime-created business identity

A Guide may capture stable business identity created during the learner session and materialize that value into later Step descriptors.

Capture is transient runtime state. Persisted Guide definitions remain unchanged.

This allows the learner to revisit entities created during the same workflow without changing the target application to manufacture test-only identifiers.

## TestCRM role

DAP.TestCRM is the permanent representative black-box target used to exercise enterprise/server-backed behavior.

The canonical business flow covers Customer → Site → Case → Lead and includes:

1. server-backed FieldChange and frame replacement;
2. validation failure with working-state preservation;
3. grid rerender/reorder and target re-resolution;
4. document reload with logical context preservation;
5. tab switching;
6. conditional target disappearance/reappearance;
7. cross-frame navigation;
8. layout shift;
9. consecutive server updates;
10. business-context switching and target isolation;
11. runtime capture of newly created business identity;
12. deletion flows;
13. centered informational Steps;
14. explicit completion.

TestCRM must not be changed merely to make DAP targeting/testing easier.

## Canonical Guides

Current persisted Guide keys:

```text
testcrm-web-canonical-workflow
testcrm-windows-canonical-workflow
```

The canonical Web Guide currently contains 55 persisted Steps. Step 55 is the Guide-owned centered completion/summary Step. Windows remains on its separately verified baseline until its next focused update.

## Current Web verification baseline — 2026-10-06

Verified product behavior:

- full runner-free Manual Web Guide: **PASS**, re-verified after the latest Hybrid/presentation/runner cleanup changes;
- DB-driven Hybrid Web Guide: **PASS**;
- canonical Web Guide: **55 persisted Steps**, including persisted completion/summary Step 55;
- extension/native-host browser path active; Playwright is absent from the active Web path;
- Runtime skips persisted disabled Steps completely;
- original persisted Step order is preserved and displayed; enabled Steps are not renumbered;
- validation/completion/progression remain Runtime-owned;
- text validation commits on blur after a real edit.

### Hybrid semi-automatic model

Hybrid exists to remove repetitive data entry without turning the Runner into a second Guide engine.

`GuideStep.IsEnabled` controls whether the Runtime executes a Step. `GuideStep.AutomationValue` is optional persisted input data for Hybrid.

Hybrid automatically fills/selects only supported value controls from `AutomationValue`. The learner still performs meaningful actions such as Search, Save, Delete, confirmation, opening records, and navigation.

Current disabled repetitive Web Steps are 24, 25, 30, 31, 42, 43, 44, and 45. Step 29 remains enabled as a meaningful status transition.

`Validation` and `AutomationValue` are deliberately separate even when their values happen to match: validation defines required Runtime truth; automation value defines permitted Hybrid input.

Hybrid manual Steps have no artificial five-second human-response deadline.

### Hybrid automatic presentation

When Hybrid presentation is active and the persisted Step has `AutomationValue`, the production Runtime includes the localized automatic-Step label in the normal bubble-presentation command.

The extension creates the label as part of the bubble itself. The Runner does not locate an already-rendered bubble, synchronize to its target, or inject DOM after the fact.

Localization key:

```text
Learner.AutomaticStep
```

Current Web extension version carrying this behavior:

```text
0.2.10
```

### Current Web runner surface

Supported execution modes:

```text
--manual
--hybrid
```

Maintenance:

```text
--reset-guide
```

Infrastructure:

```text
--published-dap <dir>
```

The historical Guided Fast, Guided Visual, Unguided, Manual-from-Step, Fast-from-Step, Visual-from-Step, `DAP_E2E_MODE`, and `DAP_E2E_BROWSER` paths are no longer part of the maintained Web runner.

## Current Web performance baseline

Manual idle Chrome measurements for the TestCRM tab:

```text
100 ms stable cadence  -> approximately 13–15% CPU
250 ms stable cadence  -> approximately 4.5% CPU
500 ms stable cadence  -> approximately 2% CPU
```

Accepted cadence:

```text
Recovery: 100 ms
Stable:   500 ms
```

A fully event-driven stable-loop experiment was rejected because it reduced CPU but degraded learner-bubble behavior.

## Bubble behavior

Current accepted Web behavior includes:

- stable bubble instance for an unchanged Step/target;
- hide while target is outside the visible viewport;
- return when target becomes visible again;
- explicit drag handle;
- `grabbing` throughout active drag;
- manual placement remains authoritative for the active Step;
- top-level proxy presentation for constrained child-frame targets;
- centered information surfaces;
- persisted Guide-owned completion/summary;
- localized Hybrid automatic-Step indicator created during normal bubble construction.

Windows follows the same interaction principles where supported by UIA/WPF.

## E2E / Runner fidelity rule

The Runner is test infrastructure only.

**Runner replaces only the learner's hands. Runtime remains the only brain of the Guide.**

The Runner may execute learner actions and observe which Runtime Step is active so it knows which action to perform next. It must not decide validation, completion, navigation success, or progression, and product protocols must not be added solely to satisfy Runner automation.

## Timeout rule

The default maximum technical E2E synchronization timeout remains 5 seconds.

Any increase above 5 seconds requires explicit user approval. This ceiling does not impose a response deadline on a human learner in Hybrid mode.

## Process isolation

Canonical runners build owned long-lived processes into unique per-run temporary directories. The Web runner preflights required ports and must never kill an arbitrary unknown process.

All Runner-owned Web child processes must be cleaned on every exit path, including an early browser-extension compatibility/handshake failure.

## Localization

DAP product UI localization is loaded from external JSON files:

```text
Localization/
  language.json
  he.json
  en.json
```

There are no compiled translation fallbacks. Guide instructional content remains Guide data. Product-owned presentation labels such as the Hybrid automatic indicator remain localization data.

## Documentation rule

Project architecture, decisions, current status, and persistent context are maintained in repository Markdown and updated with significant implementation changes.

## Immediate next work

Keep the proven Web Manual/Hybrid architecture stable. Manual has been re-verified successfully after the latest Web changes. Continue Windows work separately. Do not revive removed Web automation modes or Runner-owned Guide semantics.
