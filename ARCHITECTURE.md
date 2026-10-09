# DAP Platform Architecture

This document describes the current production architecture only. Historical implementations belong in Git history and must not be treated as current design.

## Product boundary

DAP is a .NET 8 Windows desktop product with Learner and future Instructor/Editor capabilities. Learner is launched with an explicit persisted Guide identifier; DAP derives the current runtime type from that Guide rather than from a Web/Windows launch mode. The current launcher supports Guides whose enabled targets belong to one runtime type.

Production DAP must work against closed third-party applications. Runtime behavior must not depend on customer source code, internal databases, private APIs, TestCRM implementation details, Playwright, or AI.

## Repository structure

```text
src/
  DAP.App/
  DAP.Core/
  DAP.Data/
  DAP.Data.Sqlite/
  DAP.Runtime.Web/
  DAP.Runtime.Web.NativeHost/
  DAP.Runtime.Web.Extension/
  DAP.Runtime.Windows/

test-apps/
  DAP.TestCRM/
    Server/
    Web/
    Windows/
    data/testcrm.db

tests/
  DAP.Data.Sqlite.Tests/
  DAP.TestCRM.Web.Host/
  DAP.TestCRM.Web.E2E/
  DAP.TestCRM.Windows.Host/
  DAP.TestCRM.Windows.E2E/
```

## Guide and persistence model

Guide definitions are persisted data and are the source of truth for learner execution. Core remains independent of the physical database provider. SQLite is the current provider.

The default DAP database is:

```text
C:\ProgramData\DAP\Data\DAP.db
```

A Guide Step may define a runtime-specific target, multiple anchors, validation, context, capture/completion conditions, advance mode, enabled state, and an optional persisted automation value used by Hybrid tests.

Target resolution must verify uniqueness. Missing or ambiguous targets are explicit failures; Runtime must not guess.

Disabled Steps are skipped without renumbering. Targetless centered Manual Steps are valid persisted Guide Steps.

The current TestCRM Web and Windows seeds each contain 55 persisted Steps. Step 55 is the persisted centered summary Step. Runtime does not synthesize an additional completion Step after it.

## Runtime ownership

The production Runtime owns target resolution, validation, completion observation, capture/materialization required by the Guide, and Step advancement.

A test/Hybrid action driver may perform configured learner input. It must not duplicate validation, infer business outcomes, dismiss unrelated dialogs automatically, or act as a second Guide engine.

## Web Runtime

Production Web execution uses:

```text
DAP.exe / AdapterWebGuideRuntime
    |
ExtensionWebBrowserAdapter
    |
Named Pipe
    |
DAP.Runtime.Web.NativeHost
    |
Chrome Native Messaging
    |
Manifest V3 Extension
    |
content-runtime.js
    |
Target DOM
```

The Extension is a browser adapter. Guide sequencing and progression remain in the .NET Runtime.

Production Web execution does not use Playwright or CDP.

The Extension supports dynamic DOM changes, frames, target re-resolution, learner event observation, validation signals, and bubble presentation. The Native Host bridges browser Native Messaging to the .NET process.

The Web E2E project has a separate Extension test-driver channel. Hybrid uses it for synthetic persisted-value actions. Manual and Hybrid both use a runner-owned Extension session identity for browser-lifetime observation and clean intentional browser closure; Manual sends no synthetic learner actions. The channel must never become a completion/progression oracle.

## Windows Runtime

Production Windows execution uses Microsoft UI Automation. It resolves targets from persisted descriptors, observes native interaction, evaluates Runtime-owned validation, and presents native learner bubbles.

Text editing uses natural edit/commit semantics rather than treating every intermediate value as completion. Runtime may re-resolve targets as UI changes. For input Steps, Runtime applies the one-time initial focus before Hybrid performs the configured learner action, preventing automation from racing Runtime focus setup.

## Application context and independent execution

Persisted named Application Contexts identify applications separately from Step targets. Web resolves browser contexts through the Extension. Windows currently resolves exactly one persisted Windows context at startup using `WindowTitleContains`, `AutomationId`, or `ProcessName`, requiring a unique top-level window match. The Windows canonical Guide references `crm-windows` on all 55 Steps. Multiple Windows contexts, per-Step switching, delayed discovery, and rebinding are not implemented.

Standalone TestCRM Web and Windows hosts launch their respective target applications independently of DAP. A separately launched Learner attaches through the persisted context; Guide completion must not close the target. User testing has reported successful standalone execution on both runtimes. The Windows host treats target exit code 0 as normal shutdown; the latest correction awaits runtime verification.

## Manual and Hybrid runners

The Web and Windows TestCRM runners expose normal execution as:

- `--manual --guide <GuideId>`
- `--hybrid --guide <GuideId>`

The runner loads the explicitly selected persisted Guide and passes the same Guide ID to `DAP.exe --guide <GuideId>`; it does not select the Guide implicitly.

Maintenance/path options are:

- `--reset-guide` for canonical TestCRM Guide maintenance
- `--published-dap` where a packaged DAP path is required

Manual performs no synthetic learner actions. Hybrid may apply only explicitly persisted automation values; buttons, navigation, dialogs, and other learner actions remain manual unless explicitly represented by the current persisted-data contract.

Human waiting is not subject to the five-second automated technical timeout. Intentional target closure is a clean runner termination: Windows observes target-process closure; Web observes the runner-owned Extension browser session rather than the Chrome launcher PID.

## TestCRM boundary

TestCRM is a representative external application used to exercise DAP.

Its business database is separate from DAP:

```text
test-apps\DAP.TestCRM\data\testcrm.db
```

TestCRM source may be inspected during development diagnosis, but production DAP behavior must never rely on it.

## Instructor/Editor

Instructor/Editor is the next major product phase. It must author the same persisted Guide model consumed by Learner rather than introduce a parallel model.

AI may assist development or be offered as an optional authoring aid, but production authoring and learner execution must remain deterministic and functional without AI.

## Product execution modes and Windows Hybrid

The production CLI is `DAP.exe --guide <GuideId> --mode manual|hybrid`, with Manual as default. Both modes use the same persisted Guide.

The Windows production learner identifies the application using persisted Application Context matchers and resolves each Step target using Windows UI Automation. The runtime owns bubble placement, focus, validation, completion conditions, capture, and advancement. Hybrid only enters persisted nonempty `AutomationValue` on a resolved writable ValuePattern target for an automatically validated Step. For Edit controls it focuses the target, sets and verifies the value, sends TAB, verifies focus loss, and then uses the same validation and advancement engine as Manual. All other actions remain user-operated. No TestCRM-specific driver or target source is required.

The existing SQLite GuideSteps schema stores AutomationValue, IsEnabled, ApplicationContextKey, targets, and validation. Startup initialization adds missing columns; no new migration is required.

The user reported that the Windows Hybrid run appeared correct. Full regression verification is not established. Web production Hybrid remains unsupported and explicitly rejected; test E2E drivers are separate from the product.

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

Guide execution consolidation status (in progress): both Web and Windows guide runners now invoke DAP.Core.Guides.GuideExecutionEngine for shared sequence lifecycle, capture-state ownership, and diagnostics. Platform-specific active-step loops remain in their respective runtimes and must be refactored before the single-engine architecture can be considered complete. Build and regression tests have not yet been verified on Windows.

Core GuideStepExecutionPolicy now enforces centered information-step invariants for both Web and Windows, and Web hybrid value-step eligibility. Active-step loops remain platform-specific. Build and E2E regression are pending; do not mark engine consolidation complete.

Both Web and Windows Hybrid value entry now apply the same Core GuideStepExecutionPolicy eligibility check. Platform-specific value assignment and active-step execution remain in the adapters. Full engine unification is not yet complete; build and regression verification pending.

GuideStepExecutionPolicy now classifies centered-information versus target-attached steps consistently for Web and Windows, rejecting invalid target/presentation combinations. Web automatic-validation eligibility also delegates to Core. Active-step execution loops are still separate; Windows build and end-to-end regressions remain to be verified.

Current consolidation: Core GuideStepExecutionPolicy owns automatic validation, click and target-disappearance classification, plus the committed-text validation gate and primary/completion advancement decision. Windows uses both new gates; Web uses the shared advancement decision. Platform-specific active-step loops remain; full engine unification and runtime regressions are pending.

Current architecture: GuideExecutionEngine dispatches through IGuideStepAdapter; Web and Windows provide DelegateGuideStepAdapter implementations. The engine owns step ordering, capture-token materialization, step-shape preflight and lifecycle diagnostics. Core now also owns the observed-action completion decision used for Web clicks and Windows click/target disappearance. Active-step reconciliation loops remain platform-specific; full consolidation and E2E regression tests are outstanding.

GuideExecutionEngine now dispatches centered information steps via the adapter presentation callback. The platform-specific target-step reconciliation loops are not yet consolidated.
