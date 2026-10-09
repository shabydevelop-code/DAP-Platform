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

`tests/DAP.TestCRM.Web.Host` starts the TestCRM backend and Web application independently of DAP. It builds the application projects before starting a five-second Web readiness check, and keeps its owned TestCRM processes running until the host is stopped. The host opens Chrome at `http://localhost:5200` after Web readiness succeeds.

In a separate shell, `dotnet build src\\DAP.App\\DAP.App.csproj` builds the product; `src\\DAP.App\\bin\\Debug\\net8.0-windows\\DAP.exe --guide testcrm-web-canonical-workflow` launches the Learner directly, without the E2E runner. DAP attaches to the already-open CRM through its persisted Web Application Context; it does not own the site or browser lifetime.

The independent Web Manual workflow has been verified through completion of the 55-Step Guide: the DAP process exited at Guide completion while TestCRM and Chrome remained running. Web Hybrid was separately verified with a full 55-Step user run; this Manual result does not verify Windows.

## Independent Windows application and Learner execution

`tests/DAP.TestCRM.Windows.Host` builds and starts the TestCRM backend and Windows application independently of DAP. After the backend is ready, it opens the Windows application and leaves both processes running. In another terminal, the product Learner can be launched directly with `DAP.exe --guide testcrm-windows-canonical-workflow`; the persisted `crm-windows` context resolves the already-open window. The host does not launch or own DAP. The standalone Windows host and independently launched Learner were reported successful in user testing. The target application remained open after Guide completion, and the Learner process appeared to terminate; this is not an automated process-lifetime verification. The standalone host treats normal Windows application exit code 0 as a clean shutdown; the fix still requires a fresh user runtime test.

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
- Platform-specific startup, browser Extension or Windows UIA interaction, guide verification and target-specific cleanup remain in their respective runner `Program.cs` files. The E2E orchestration is not yet fully centralized; these entry points must not be removed merely because their common CLI and process-exit policies are shared.
- Changes require a fresh build and Ctrl+C regression on both runners. No post-change build or E2E pass has been reported yet.

## Unified E2E project migration

- `tests/DAP.E2E/` provides one CLI entry point with `--platform web|windows`; its `Runner/E2eRunner.cs` owns process execution/cancellation, and `Platforms/PlatformProject.cs` resolves the legacy platform runner during migration.
- **Transitional only:** the unified entry point currently delegates to the existing Web/Windows E2E projects. It is not yet a consolidated orchestration engine or replacement for the platform-specific runner code.
- Do not delete `tests/DAP.TestCRM.Web.E2E/` or `tests/DAP.TestCRM.Windows.E2E/` until platform adapters, shared orchestration and all regression paths have moved and passed verification.
- The new project has not yet been built or run on the user's machine.
- The new thin entry point and its extracted runner/route components still require a post-change build and runtime verification.
- Unified `DAP.E2E` now compiles the existing shared `E2eRunOptions` and `OwnedProcessCleanup` components; the entry point validates forwarded options and its orchestration delegates process cleanup to the shared helper. The platform-specific scenario implementations remain in the legacy projects, so migration and deletion are still pending.
- The most recent changes require a fresh build and regression verification.
- `tests/Shared/E2ePortGuard.cs` now centralizes the previously duplicated TCP port preflight in the Web and Windows E2E runners, preserving platform-specific diagnostic messages. The shared file is also linked by `DAP.E2E`.
- The platform scenario drivers and their large runner bodies have **not** yet moved into `DAP.E2E`. The old projects are still required and must not be deleted. The latest port-preflight refactor requires fresh builds on both platforms.
- `DAP.E2E/Platforms/PlatformProject.cs` now declares `IE2ePlatform` with Web and Windows adapters; the unified entry point uses the adapter to locate the platform-specific runner. These adapters are transitional launch adapters, not migrated scenario implementations. Both legacy projects remain required. The latest change awaits a fresh build.
