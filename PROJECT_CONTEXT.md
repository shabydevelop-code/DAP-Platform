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

Current repository seeds contain 54 Steps for both platforms.

## Current Web verification baseline — 2026-10-06

Verified:

- runner-free manual Web Guide: 54/54 PASS;
- Guided Fast automated Web Guide: 54/54 PASS;
- extension/native-host browser path active;
- automatic Chrome/Edge profile discovery;
- production bubble from SQLite Guide data;
- invalid committed value rejected;
- text validation waits for blur;
- automatic Step transition;
- full canonical workflow completion;
- no Guide/DB change was required to make Guided automation pass.

This is important evidence that persisted Guide data and production Runtime semantics are sufficient. Automated modes must not introduce parallel target/validation/completion knowledge.

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
- explicit Guide completion.

Windows follows the same interaction principles where supported by UIA/WPF.

## Canonical run modes

Web runner public modes:

```text
--manual
--guided
--manual-from-step N
--unguided
--visual-from-step N
```

Automated Guided presentation mode is selected through:

```text
DAP_E2E_MODE=fast
DAP_E2E_MODE=visual
```

Fast and Visual share the same business/learner action path. Visual may add cursor movement and pacing only.

Focused from-Step modes execute the actual preceding workflow as bootstrap rather than fabricating application state.

## E2E fidelity rule

The runner may execute actions and inspect results, but it must not add semantic facts that the persisted Guide/Runtime should already know.

Forbidden E2E fixes include:

- hidden target selectors used to compensate for Guide targeting;
- hidden validation/completion conditions;
- special transition rules;
- application-specific bypasses that are unavailable to a real learner.

If manual execution succeeds and automation fails, investigate the automation boundary first.

## Timeout rule

The default maximum E2E timeout remains 5 seconds.

Any increase above 5 seconds requires explicit user approval before implementation, including temporary diagnostics.

## Process isolation

Canonical runners build owned long-lived processes into unique per-run temporary directories under:

```text
%TEMP%\DAP\E2E\Web\<run-id>
%TEMP%\DAP\E2E\Windows\<run-id>
```

The Web runner preflights ports 5200 and 5201. It must never kill an arbitrary unknown process merely because the port is occupied.

## Localization

DAP product UI localization is loaded from external JSON files:

```text
Localization/
  language.json
  he.json
  en.json
```

There are no compiled translation fallbacks. Guide instructional content remains Guide data.

## Documentation rule

Project architecture, decisions, current status, and persistent context are maintained in repository Markdown and updated with significant implementation changes.

## Immediate next work

1. Verify `manual-from-step` on the unified extension-native Web path.
2. Verify `visual-from-step` using the same action semantics.
3. Verify `unguided` without introducing duplicate Guide knowledge.
4. Preserve the runner-free manual 54-Step path as the product acceptance reference.
5. Keep browser/runtime performance work subordinate to learner correctness.
