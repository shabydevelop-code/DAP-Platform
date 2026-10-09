# Current Status

This document contains only the current implementation state. Historical milestones and retired runner modes are intentionally excluded.

## Current baseline

- .NET 8 production architecture is implemented for Web and Windows Learner runtimes.
- SQLite is the current persistence provider behind provider-independent Core/Data contracts.
- Default DAP database: `C:\ProgramData\DAP\Data\DAP.db`.
- TestCRM uses its own local business database: `demos\Shared\data\testcrm.db`.
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

The Web E2E runner uses the Extension test-driver channel for browser-session observation; production Runtime owns Hybrid value application. Manual and Hybrid each use a dedicated Extension browser-session identity so intentional browser closure is detected independently of the Chrome launcher process and ends the run cleanly. Manual sends no synthetic learner actions. Runtime remains the sole owner of completion and Step progression.

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

`demos/Web/Launcher` starts the TestCRM backend and Web application independently of DAP. It builds the application projects before starting a five-second Web readiness check, and keeps its owned TestCRM processes running until the host is stopped. The host opens Chrome at `http://localhost:5200` after Web readiness succeeds.

In a separate shell, `dotnet build src\\DAP.App\\DAP.App.csproj` builds the product; `src\\DAP.App\\bin\\Debug\\net8.0-windows\\DAP.exe --guide testcrm-web-canonical-workflow` launches the Learner directly, without the E2E runner. DAP attaches to the already-open CRM through its persisted Web Application Context; it does not own the site or browser lifetime.

The independent Web Manual workflow has been verified through completion of the 55-Step Guide: the DAP process exited at Guide completion while TestCRM and Chrome remained running. Web Hybrid was separately verified with a full 55-Step user run; this Manual result does not verify Windows.

## Independent Windows application and Learner execution

`demos/Windows/Launcher` builds and starts the TestCRM backend and Windows application independently of DAP. After the backend is ready, it opens the Windows application and leaves both processes running. In another terminal, the product Learner can be launched directly with `DAP.exe --guide testcrm-windows-canonical-workflow`; the persisted `crm-windows` context resolves the already-open window. The host does not launch or own DAP. The standalone Windows host and independently launched Learner were reported successful in user testing. The target application remained open after Guide completion, and the Learner process appeared to terminate; this is not an automated process-lifetime verification. The standalone host treats normal Windows application exit code 0 as a clean shutdown; the fix still requires a fresh user runtime test.

## Verification state

Current Manual/Hybrid behavior is persisted-data-driven on both runtimes. Windows Hybrid input focus synchronization is verified for the first input Step. Intentional Windows target closure and Web browser-session closure terminate cleanly rather than being reported as Runtime failures. Web Hybrid emits the browser-session closure message once through its owning termination path; Web Manual retains its passive clean-closure path. Web Manual browser-lifetime observation is passive and does not automate learner actions.

The user confirmed a complete 55-Step Web Hybrid canonical run. Windows and other Web modes require separate verification.

## Production execution status

Windows startup now restores a minimized target window when needed, requests foreground activation and verifies it within five seconds before starting the Guide. This is shared by Manual and Hybrid. The change has not been verified in a local Windows run. Equivalent browser-window/tab activation is not yet implemented in the Web extension; do not claim startup focus parity.

The same persisted Guide supports `DAP.exe --guide <GuideId> --mode manual|hybrid` (Manual is the default). Windows production Hybrid reads AutomationValue from SQLite and enters it through UIA ValuePattern on resolved writable targets. For Edit controls it verifies focus, value entry, and TAB blur before the runtime evaluates the existing validation and completion conditions. Steps without an automation value remain user-operated. The product path does not call the TestCRM E2E driver.

The user reported successful full 55-step Hybrid runs for both Web and Windows before the latest Web runtime rename; post-rename end-to-end regression is not yet confirmed. The existing database schema and automatic column initialization support this feature without a new migration. Web production Hybrid applies persisted values through the browser Extension. The user confirmed successful completion of the full 55-Step Web Hybrid canonical Guide. The Extension checks content-script generation before routing commands; reload the installed unpacked Extension after source changes.

### Learner session termination and local build guard

Each DAP.exe invocation owns one learner session. After the guide runtime and cleanup return, the WPF application shuts down and explicitly terminates its own process so its loaded DLLs are released. The DAP.App build checks only processes running the exact DAP.exe from that build output; if one is still active, the build stops immediately with its PID and an actionable message rather than repeatedly retrying DLL copies. Copy retries are disabled for DAP.App. No other DAP processes are killed, and no global session registry is introduced. These changes require validation on Windows; a stale process from an older executable must be closed once before rebuilding.

## Shared guide execution — current implementation

- `DAP.Core.Guides.GuideExecutionEngine` owns Guide ordering, capture-token materialization, step-shape preflight, disabled-step handling, centered-information dispatch, and lifecycle diagnostics through `IGuideStepAdapter`.
- `UnifiedGuideStepEngine` runs the active-step observation loop for Web and Windows, delegates context/readiness and completion decisions to shared `GuideActiveStepState`, and invokes platform-specific reconciliation callbacks. `GuideActiveStepState` also owns hybrid-value application state and shared wait transitions; platform-specific event observation, target resolution, and bubble effects remain in the adapters.
- `GuideStepExecutionPolicy`, `GuideValidationPolicy`, and `GuideCompletionPolicy` contain runtime-independent classification and validation rules. Web DOM events and Windows UI Automation observations remain platform-specific.
- Both runtimes now call the Core presentation-availability policy for missing targets; Windows also delegates the invisible-target state to Core. This does not replace platform-specific visibility inspection or rendering.
- Web `WebGuideStepRuntime` and Windows `WindowsGuideRuntime` retain platform-specific reconciliation callbacks and UI effects. Their presence alone does not establish duplicated learner policy; shared decisions reside in Core, and remaining decisions require responsibility-by-responsibility review before further extraction or deletion.
- The user confirmed a successful local `DAP.App` build after the Web step-runtime rename and application-host reference correction. Manual end-to-end regression after these changes remains unverified.
- Web startup browser activation parity with Windows remains an open item. The user reported successful full 55-step Web and Windows Hybrid runs before the latest Web runtime rename; post-rename end-to-end regression and Manual runs are not confirmed.

## E2E runner responsibility separation

- Both Web and Windows E2E entry points use `tests/Shared/E2eRunOptions.cs` for guide selection, mode validation and published-package arguments, and `tests/Shared/OwnedProcessCleanup.cs` for Ctrl+C/process-exit child cleanup.
- Platform-specific startup, browser Extension or Windows UIA interaction, and guide verification now reside in the unified E2E project's platform scenario files.
- Ctrl+C and packaged diagnostics regression still require verification after the latest script changes.

## Unified E2E project

- `tests/DAP.E2E/` is the unified Web/Windows E2E project. Both platform scenarios execute in-process and all four platform-specific helper sources reside under `tests/DAP.E2E/Platforms/`.
- Shared option parsing, port preflight, and owned-process cleanup remain in `tests/Shared/`.
- The user verified successful unified-project builds and Manual E2E runs on both Web and Windows after helper migration.
- The obsolete project-launching adapter and subprocess runner have been removed from `DAP.E2E`.
- Customer diagnostic publication now targets `tests/DAP.E2E/DAP.E2E.csproj` once, at `Diagnostics/Runners/Unified`. Initialization and run scripts now use `--platform web|windows` and supported Manual/Hybrid modes.
- The user reported successful packaged Web and Windows Manual diagnostics after the unified runner migration. Both legacy E2E project directories were removed after their platform-specific sources had been migrated to `tests/DAP.E2E`. Post-deletion build and package publish remain to be verified.
- Learner completion behavior and Web/Windows target-app closure differences remain deferred. Web startup now invokes owned-process cleanup even when an exception occurs before Guide execution. The package publisher now rejects active processes from the output directory before deleting or publishing files.
- Packaged Web Manual diagnostics failed immediately after Chrome startup because the Extension test-driver pipe connected before the session tab was visible (`testPing`: zero matching tabs). The unified Web scenario now retries only this transient zero-tab response at 100 ms intervals within the existing 5-second connection/ping budget; ambiguous tabs and unrelated failures are not retried. The user subsequently reported packaged Web Manual diagnostics appearing correct; a fresh post-cleanup regression is still required.

- E2E-owned target cleanup parity (2026-10-09): the unified Web runner now issues the extension's existing session-scoped `testCloseTab` command on exit (success, early exit, or failure) and no longer kills the Chrome launcher process, which may belong to a reused browser instance. The Windows runner already closes its own target-app and backend processes. This is runner ownership cleanup, not a production learner instruction to close the target application. The user reported successful local build/publish and tab-closure cleanup; a separate complete end-of-guide tab-cleanup regression is not yet confirmed.

- Web HYBRID Step 13 regression (2026-10-09): automatic status value 'בטיפול' was initially followed by a duplicate manual bubble. `WebGuideStepRuntime` now reconciles persisted validation and completion conditions after a successful Hybrid write even if iframe replacement loses the DOM commit event. User confirmed the fix and a complete Web HYBRID run through all 55 steps: PASS. This confirms Web HYBRID end-to-end only; no new Windows result is implied.

- E2E persisted-guide separation (2026-10-09): normal Web runs no longer rename the persisted guide or compare its step count with `DapTestCrmGuideSeed`; normal Windows runs no longer compare with `DapTestCrmWindowsGuideSeed`. Both consume DB-persisted steps and validate nonempty/contiguous order; seed classes remain only for explicit `--reset-guide`. The user reported successful local build/publish after these changes; full post-change Web/Windows guide regressions are not yet separately confirmed.

- Web intentional tab closure (2026-10-09): `DAP.App/App.xaml.cs` now recognizes the specific extension test-driver error reporting zero tabs for the E2E session and exits with interrupted code 2 without showing the `DAP - Session Error` dialog. E2E already probes for a missing tab and cleans up runner-owned resources; no PASS is reported for an interrupted guide. Local build/publish and intentional Chrome tab-closure regression were reported successful by the user on 2026-10-09: PASS, no session-error dialog. The interrupted run must not be reported as a completed 55-step PASS. Other failures still show error dialogs.

- Production/TestCRM coupling audit (2026-10-09): `src/DAP.App/DAP.App.csproj` references only DAP product projects (Core, Data, SQLite, Web, Windows), with no direct `tests` or `test-apps` project references. `DapApplicationHost` loads persisted Guide steps and application contexts from SQLite. `GuideExecutionEngine` contains no direct TestCRM/E2E references in the inspected file. However production-tree Web Extension `service-worker.js` includes E2E-only session tab resolution (`dap-e2e-session`) and test-driver commands including `testPing`, `testNavigate`, and `testCloseTab`; `src/DAP.Runtime.Web.NativeHost/Program.cs` starts a dedicated `dap-web-e2e-v1` pipe and forwards test-driver messages; `src/DAP.App/App.xaml.cs` identifies tab closure by parsing a test-driver-specific error message. Thus source-level product/test isolation is NOT complete even though the main app project references are clean. The current `scripts/Publish-Customer-Package.ps1` publishes TestCRM and E2E under Diagnostics. Follow-up: separate production publication and E2E-only extension/native-host transport; replace test-error-string-based lifecycle handling with a generic typed/session-state signal, preserving session-safe cleanup. No runtime code removed or refactored in this audit; no local build/regression claim.
