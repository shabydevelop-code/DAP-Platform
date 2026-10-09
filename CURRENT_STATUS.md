# Current Status

This document contains only the current implementation state. Historical milestones and retired runner modes are intentionally excluded.

## Current baseline

- .NET 8 production architecture is implemented for Web and Windows Learner runtimes.
- SQLite is the current persistence provider behind provider-independent Core/Data contracts.
- Default DAP database: `C:\ProgramData\DAP\Data\DAP.db`.
- TestCRM uses its own local business database: `test-apps\DAP.TestCRM\data\testcrm.db`.
- Current Web and Windows TestCRM Guide seeds contain 55 persisted Steps.
- Step 55 is the persisted centered Guide summary.
- Disabled persisted Steps are supported without renumbering.
- Persisted `AutomationValue` is supported for Hybrid value-entry actions.
- Product Learner launch uses `DAP.exe --guide <GuideId>`; the runtime is selected from the persisted Guide. The current launcher accepts one enabled target runtime type per Guide.
- The browser Extension is infrastructure-only and has no popup GUI.

## Web

Production Web Runtime no longer depends on Playwright.

The active production path is:

```text
DAP.exe
-> AdapterWebGuideRuntime
-> ExtensionWebBrowserAdapter
-> Native Host
-> Browser Extension
-> target DOM
```

The Native Host and unpacked Extension are implemented. Chrome Native Messaging registration has been verified during development.

The Web E2E runner uses the Extension test-driver channel for Hybrid synthetic actions. Manual and Hybrid each use a dedicated Extension browser-session identity so intentional browser closure is detected independently of the Chrome launcher process and ends the run cleanly. Manual sends no synthetic learner actions. Runtime remains the sole owner of completion and Step progression.

## Windows

Production Windows Runtime uses Microsoft UI Automation.

A complete human Manual run has demonstrated persisted-data-driven learner execution without a parallel test completion engine. Hybrid uses persisted automation values only for configured value controls and synchronizes with Runtime progression. Runtime applies one-time initial input focus before Hybrid value actions, and intentional target-window closure ends the runner cleanly.

A first Windows application-context resolver is implemented: when a Guide defines exactly one Windows context, DAP resolves an already-open top-level window by persisted `WindowTitleContains`, `AutomationId`, or `ProcessName` matchers and requires a unique match. Guides without a Windows context retain the explicit `--window-automation-id` fallback. The Windows context-based canonical learner and runner have been reported successful in user testing. The Windows canonical Guide seed now assigns context `crm-windows` to its 55 Steps with `AutomationId: TestCrmMainWindow`. Reset persists that context and the Windows E2E runner omits the window CLI argument when a persisted Windows context exists. Multiple Windows contexts, per-Step context switching, delayed discovery, and rebinding remain unimplemented.

## Canonical runner contract

Current TestCRM execution contract:

```text
--manual --guide <GuideId>
--hybrid --guide <GuideId>
```

The supplied persisted Guide ID is loaded by the runner and passed unchanged to the product Learner. The runner does not select the Guide implicitly.

Maintenance/path options:

```text
--reset-guide
--published-dap
```

## Independent Web application and Learner execution

`tests/DAP.TestCRM.Web.Host` starts the TestCRM backend and Web application independently of DAP. It builds the application projects before starting a five-second Web readiness check, and keeps its owned TestCRM processes running until the host is stopped. The host opens Chrome at `http://localhost:5200` after Web readiness succeeds.

In a separate shell, `dotnet build src\\DAP.App\\DAP.App.csproj` builds the product; `src\\DAP.App\\bin\\Debug\\net8.0-windows\\DAP.exe --guide testcrm-web-canonical-workflow` launches the Learner directly, without the E2E runner. DAP attaches to the already-open CRM through its persisted Web Application Context; it does not own the site or browser lifetime.

The independent Web Manual workflow has been verified through completion of the 55-Step Guide: the DAP process exited at Guide completion while TestCRM and Chrome remained running. This does not constitute a new Hybrid or Windows regression result.

## Independent Windows application and Learner execution

`tests/DAP.TestCRM.Windows.Host` builds and starts the TestCRM backend and Windows application independently of DAP. After the backend is ready, it opens the Windows application and leaves both processes running. In another terminal, the product Learner can be launched directly with `DAP.exe --guide testcrm-windows-canonical-workflow`; the persisted `crm-windows` context resolves the already-open window. The host does not launch or own DAP. The standalone Windows host and independently launched Learner were reported successful in user testing. The target application remained open after Guide completion, and the Learner process appeared to terminate; this is not an automated process-lifetime verification. The standalone host treats normal Windows application exit code 0 as a clean shutdown; the fix still requires a fresh user runtime test.

## Verification state

Current Manual/Hybrid behavior is persisted-data-driven on both runtimes. Windows Hybrid input focus synchronization is verified for the first input Step. Intentional Windows target closure and Web browser-session closure terminate cleanly rather than being reported as Runtime failures. Web Hybrid emits the browser-session closure message once through its owning termination path; Web Manual retains its passive clean-closure path. Web Manual browser-lifetime observation is passive and does not automate learner actions.

Do not claim a fresh full-guide regression unless such a run has actually been completed.

## Production execution status

Windows startup now restores a minimized target window when needed, requests foreground activation and verifies it within five seconds before starting the Guide. This is shared by Manual and Hybrid. The change has not been verified in a local Windows run. Equivalent browser-window/tab activation is not yet implemented in the Web extension; do not claim startup focus parity.

The same persisted Guide supports `DAP.exe --guide <GuideId> --mode manual|hybrid` (Manual is the default). Windows production Hybrid reads AutomationValue from SQLite and enters it through UIA ValuePattern on resolved writable targets. For Edit controls it verifies focus, value entry, and TAB blur before the runtime evaluates the existing validation and completion conditions. Steps without an automation value remain user-operated. The product path does not call the TestCRM E2E driver.

The user reported the Windows Hybrid run appeared correct. A full-guide regression is not independently documented, so do not mark one PASS. The existing database schema and automatic column initialization support this feature without a new migration. Web production Hybrid now has an extension-backed persisted-value executor; build and full runtime regression are pending.

### Learner session termination and local build guard

Each DAP.exe invocation owns one learner session. After the guide runtime and cleanup return, the WPF application shuts down and explicitly terminates its own process so its loaded DLLs are released. The DAP.App build checks only processes running the exact DAP.exe from that build output; if one is still active, the build stops immediately with its PID and an actionable message rather than repeatedly retrying DLL copies. Copy retries are disabled for DAP.App. No other DAP processes are killed, and no global session registry is introduced. These changes require validation on Windows; a stale process from an older executable must be closed once before rebuilding.

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
