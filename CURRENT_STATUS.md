# Current Status

Last updated: 2026-10-01

## Current phase

Active E2E validation and stabilization of the permanent DAP.TestCRM PeopleSoft-Web test target.

## Current E2E baseline

- `tests/DAP.TestCRM.E2E/Program.cs` contains the representative Customer -> Site -> Case -> Lead workflow.
- The workflow currently passes end-to-end, including ten validated business-facing scenarios, dynamic Lead deletion, and Case deletion.
- Playwright default timeout is 5 seconds for the E2E runner.
- E2E execution supports two modes through `DAP_E2E_MODE`:
  - `fast` (default): skips artificial human/visual delays and TestCRM's artificial server-thinking delay.
  - `visual`: preserves cursor movement, typing delays, processing feedback, and artificial server delay for demonstration.
- Real readiness conditions remain active in both modes. The E2E does not replace actual server/DOM/frame readiness with fixed sleeps.
- PeopleSoft-style Content iframe replacement is handled by re-resolving the active frame and waiting for real route readiness.
- Scenario coverage now includes Case FieldChange + iframe replacement, server validation with unsaved-value preservation, Grid rerender/reorder + target re-resolution, Content-document reload with preserved Case context, CRM tab switching with preserved business context, conditional target disappearance/reappearance with re-resolution, cross-frame Header-to-Content navigation, Layout Shift + target re-resolution, consecutive server updates with final-state re-resolution, and business-context switching with target isolation.
- The E2E scenarios use real UI/application behavior; no TestCRM-specific route-persistence workaround is used for the validated baseline.
- The permanent TestCRM server still exposes the `מעבד...` activity indicator and artificial server delay in normal/visual behavior; the E2E fast mode bypasses those artificial delays only for test execution.
- Frame polling remains 100ms.
- The E2E runner uses a 5-second default Playwright timeout; this was intentionally restored after rollback validation.


## Confirmed product architecture

- Single Windows desktop application: DAP.exe.
- .NET 8 + WPF.
- Learner and Editor modes.
- Hebrew and English GUI with RTL/LTR support.
- Web and Windows guide support.
- Hybrid Web/Windows guides.
- Microsoft Playwright for .NET as the Web Runtime.
- Microsoft UI Automation as the Windows Runtime.
- No Python dependency in the production application.
- Database-independent data layer.
- SQLite as the first database provider.
- .NET 8 Desktop Runtime is assumed on target machines.
- Markdown files are the persistent source for project context, architecture, decisions, requirements, and progress.
- Guide navigation is context-aware: action Steps advance on successful validation; informational Steps may use manual Next; Previous is available only when the prior Step is safely renderable in the current runtime/application context.

## Historical validation

Earlier isolated experiments validated important Web runtime behaviors including browser interaction, frames, DOM replacement, server-side field changes, reloads, navigation, API-backed saves, target re-resolution, and grid interaction.

These results inform the product architecture but are not part of the production implementation and must not define production packaging or dependencies.

## Test target application

A permanent server-backed test application now exists at `test-apps/DAP.TestCRM`.

It models Customer -> Sites -> Cases / Leads and supports opening and creating records. Data is persisted in SQLite. Browser operations use HTTP API requests, grids are sorted server-side through explicit sort parameters, and breadcrumbs provide navigation back to the customer portal from all record screens. It deliberately includes asynchronous server round-trips, dynamic DOM replacement, tabs, grids, navigation, and create/edit/save flows so production DAP runtime behavior can be derived and tested against realistic application behavior. Current PeopleSoft-Web coverage also includes conditional targets that disappear/reappear, server-controlled disabled/enabled fields, repeated grid actions, off-screen targets in a scrollable business record, target movement caused by conditional layout, Content iframe element replacement, validation-driven layout changes, transient generated DOM IDs, server-side grid rerender/reorder, full Content-document reload with preserved logical Case context, and leaving/returning to the same business record by stable identity.

## Current validation notes

The current representative E2E is the validated baseline for further test expansion. New scenarios should be added without regressing the existing representative flow or reintroducing fixed artificial waits as synchronization mechanisms.

## Not implemented yet

- Solution/project structure (partially started: `src/DAP.Core` now exists; full solution structure is not yet created).
- WPF shell (initial `DAP.App` executable host now exists; user-facing shell UI is not implemented).
- Learner UI.
- Editor UI.
- Shared guide domain model (target-resolution foundation started; Guide/Step/Bubble/Validation model still pending).
- Data abstraction and SQLite provider.
- Web Runtime integration using Playwright for .NET.
- Windows Runtime integration.
- Recorder.
- Localization resources.
- Production deployment/bootstrap validation.
- Automated tests.


## Current implementation scope

Architecture/Core scope is Web + Windows. The active implementation scope is Web only: Learner Web Runtime, Playwright target resolution, and Web bubbles against DAP.TestCRM. Windows/UIA bubble implementation is intentionally deferred; it will later implement the same shared Core contracts.

## Persistence foundation
SQLite default database location on Windows is `%ProgramData%\DAP\Data\DAP.db` (normally `C:\ProgramData\DAP\Data\DAP.db`). The application/provider may override this with `DAP_DATABASE_PATH`; Core must not depend on either the path or SQLite.


The shared Core now includes initial runtime-neutral `ValidationDefinition`, `BubbleDefinition`, and `GuideStep` models. `DAP.Data` defines the guide-step repository abstraction. `DAP.Data.Sqlite` now contains the SQLite connection/bootstrap implementation, schema initialization, and `SqliteGuideStepRepository` with transactional save/load mapping for TargetDescriptor, ordered multiple Anchors, FrameContext, Bubble, Validation, and Step advance mode. Core remains independent from SQLite. A standalone `DAP.Data.Sqlite.Tests` round-trip executable uses a temporary database and does not contain production persistence implementation.

The schema is a provider implementation detail, not the domain contract. Current implementation work remains Web-only even though persisted Runtime/Target data is designed to support both Web and Windows.

## First Web bubble presenter

Production `DAP.Runtime.Web` now contains the first `WebBubblePresenter`. It resolves a GuideStep target through `WebTargetResolver`, injects the bubble into the resolved target document/frame, and keeps it positioned on scroll/resize. It does not live in the E2E project. A TestCRM-specific first-step definition exists only as an E2E fixture and is intentionally excluded from production Data/Runtime code.

Bubble visual styling is now separated from GuideStep persistence: `WebBubbleTheme` owns the Web presentation defaults (colors, typography, border, radius, spacing, shadow), while persisted `BubbleDefinition` continues to carry only step-specific content and placement. `WebBubblePresenter` consumes the theme and cleans up prior bubble observers/listeners before replacing a bubble in the same document. No per-step colors or CSS were added to SQLite.

The Web bubble now makes target association explicit: `WebBubblePresenter` highlights the resolved DOM element itself using runtime-applied outline/shadow styling and renders a directional pointer on the bubble. It does not use a separate target-sized overlay, avoiding box-model/border alignment errors. Highlight/pointer appearance is owned by `WebBubbleTheme`, not persisted per Step. The presenter's cleanup restores the target element's prior inline outline/outline-offset/box-shadow values and removes its observers/listeners.

The TestCRM E2E harness is now wired to the production `DAP.Data.Sqlite` and `DAP.Runtime.Web` projects. Before the existing CRM workflow starts, it initializes a temporary DAP SQLite database, persists the first TestCRM GuideStep, reloads it through `SqliteGuideStepRepository`, resolves its real target through `WebTargetResolver`, presents it through `WebBubblePresenter`, and asserts the rendered bubble content. The fixture definition remains test-only; the persistence and bubble implementation are production code.


The TestCRM E2E now starts the production `WebLearnerRuntime` for the SQLite-loaded first Step, verifies its initial bubble, explicitly reloads the active Content document, and then requires the runtime to restore the same Step bubble on the newly resolved `[name='name']` target without any test-side call to `WebBubblePresenter`. This is the first direct E2E assertion of the production lifecycle across document replacement. The test change is committed but must still be executed locally before it is considered a passing regression result.

`DAP.Runtime.Web.Learner.WebLearnerRuntime` now provides the first production active-Step lifecycle. It reconciles the active Step every 100ms, always re-resolving from its `TargetDescriptor`; a replaced iframe/document or rerendered target is therefore reacquired instead of retaining stale Playwright identity. `WebBubblePresenter.EnsureShownAsync` is idempotent for the same Step and live DOM target, so reconciliation does not recreate/flicker the bubble on every pass. If the target is missing or ambiguous, the runtime hides stale guidance and continues waiting rather than guessing. Playwright navigation/frame races are retried by the next reconciliation cycle. `WebValidationEvaluator` now evaluates runtime-specific Web validation. The first supported rule is `value-not-empty`. For `AutomaticOnValidation` Steps, `WebLearnerRuntime` evaluates the same uniquely resolved target returned by bubble presentation; on success it hides the bubble and completes `RunActiveStepAsync`. Unsupported validation kinds fail explicitly. Manual Steps are not auto-completed.

A runtime-neutral `StepContextDefinition` is now part of `GuideStep` and is persisted by SQLite. The Web adapter implements `WebStepContextGuard`; initial supported Web context predicates are `url-equals`, `url-contains`, `url-fragment-equals`, and `css-exists`, evaluated against the live frame identified by the Step's `FrameContext`. `WebLearnerRuntime` checks this guard before target resolution. This allows iframe/document replacement within the same logical route while suppressing the active Step after the user leaves that business context. The TestCRM customer-search fixture uses the stable screen marker `#customer-search` rather than coupling the Step to a route fragment, and E2E now asserts that the bubble disappears after navigation leaves customer search. These new changes require local execution before being considered passing regression coverage.

## Next milestone

The runtime-neutral target-resolution Core has now been started in `src/DAP.Core`. It defines `TargetDescriptor`, `Locator`, `Anchor`, `FrameContext`, runtime identity, and explicit `Resolved` / `NotFound` / `Ambiguous` resolution results. The Core has no Playwright, UIA, SQLite, or test-project dependency.

The target-resolver contract now exists in `DAP.Core`, and the initial Playwright-backed `WebTargetResolver` exists in `DAP.Runtime.Web`. The Web implementation re-resolves frame hierarchy per resolution request, filters candidates through all configured anchors, and returns explicit `NotFound` / `Resolved` / `Ambiguous` outcomes. Playwright remains outside Core. Initial anchor-relation evaluation currently supports CSS anchors; this is a deliberate first implementation boundary, not a Core limitation.

Next: add focused production resolver tests outside the E2E implementation, then introduce the shared `Validation`, `Bubble`, and `GuideStep` Core models before wiring bubble presentation to the Learner Runtime. Bubble rendering remains production runtime code and must not be implemented inside the E2E project. The existing ten-scenario E2E baseline remains the regression baseline.


### Validation coverage
The validated E2E baseline already includes a real CRM value-validation flow in the Case workflow: changing status to `סגורה` makes `closeReason` required; an attempted save without that value is rejected by server validation while unsaved Subject and Description values are preserved; supplying `closeReason` (`טופל`) then allows the save to succeed. This validation currently runs without DAP bubbles and is part of the underlying CRM/E2E behavior, not a bubble-specific test.


### Target resolution model
A target is represented by a runtime-neutral TargetDescriptor rather than a single selector. It identifies the target through a primary locator plus zero or more anchors/context constraints. Resolution must discover candidates, apply the anchors, verify uniqueness, and return an explicit ambiguous/not-found result rather than guessing. The descriptor also carries the runtime and frame context required by the corresponding Web or Windows adapter. This model is intended to support re-resolution after DOM changes, iframe replacement, grid rerender/reorder, layout shifts, target disappearance/reappearance, and equivalent Windows UI changes.


## DAP executable host

`src/DAP.App` now exists as a real `net8.0-windows` WPF `WinExe` with assembly name `DAP`. It references Core, Data, SQLite, and the Web Runtime and acts as the production composition root. `--check` initializes the configured SQLite provider, composes the Web Learner runtime services, writes `%TEMP%\\DAP\\dap-check.txt`, and displays a WPF confirmation dialog so infrastructure-check success is visible even though DAP is a `WinExe`. `--learner-web <guide-id> --cdp <endpoint> [--page-url-contains <text>]` now implements the first independent Web attachment path through Playwright `ConnectOverCDPAsync`: it requires exactly one matching Chromium page, loads the persisted guide Steps, and runs the first Step from inside `DAP.exe`. This path still requires local build/runtime validation. The E2E harness now launches Chromium with a per-run CDP port, persists the fixture Step into a temporary SQLite database, starts `DAP.App` through a separate `dotnet` process with that database path and CDP endpoint, and expects the external DAP process to own bubble re-resolution/context behavior. The former in-process `WebLearnerRuntime` wiring has been removed from this E2E path. The E2E continues to host the Learner runtime in-process only as behavioral regression coverage. No separate Bubble.exe is planned; bubble lifecycle belongs to DAP.exe.


## Automatic Web Step validation

The first production validation path is now implemented. `ValidationDefinition` remains runtime-neutral in Core; `WebValidationEvaluator` owns Web interpretation. The external-process TestCRM E2E now fills the customer-name target and requires the active bubble to disappear from successful `value-not-empty` validation before CRM navigation changes logical context. It then verifies that the completed Step does not reappear after route navigation. These new validation assertions require local execution before being marked passing.


## Ordered Web guide lifecycle

`WebGuideRuntime` now owns ordered guide orchestration while `WebLearnerRuntime` remains responsible for one active Step. `DAP.exe` passes the complete persisted Step list to the guide runtime instead of running only `steps[0]`. The TestCRM fixture now contains two persisted Steps: customer-name (`value-not-empty`) followed by customer-search-button (`clicked`). The E2E requires the second bubble to appear after Step 1 completes. The two-Step transition is locally verified passing: Step 1 automatic validation advances to Step 2, and the completed Step remains inactive after route change.


## Direct DAP.exe E2E startup

The TestCRM E2E now separates build time from runtime startup. It builds `DAP.App` explicitly, then launches the resulting `DAP.exe` directly instead of using `dotnet run` as the learner process. The test reports elapsed time from starting the executable until the first production bubble is observed. This matches the deployed product process boundary more closely and prevents MSBuild time from being mistaken for DAP runtime startup latency. Local timing is now measured. A representative visual run reported 7648 ms from DAP.exe process start until the first bubble was observed. Internal instrumentation isolated the dominant startup cost: SQLite initialization completed at 233 ms, composition at 234 ms, Playwright.CreateAsync completed at 6628 ms, CDP connection at 6731 ms, page selection at 6733 ms, guide load at 6749 ms, and guide runtime start at 6750 ms. Thus roughly 6.4 seconds of that run were spent inside Playwright.CreateAsync, while CDP connection and guide persistence were comparatively small. Bubble target-resolution/DOM-presentation instrumentation has been added next so the remaining post-guide-start interval can be separated from E2E observation latency before optimization.


## Interrupted E2E process isolation

The TestCRM E2E now builds DAP.App into a unique temporary E2E-owned output directory for each run instead of the project's normal bin directory. This prevents an orphaned DAP.exe from an interrupted run from locking the next build. The E2E also registers a process-exit cleanup safety net that terminates only the DAP process tree created by that test run, covering Ctrl+C/process termination paths in addition to the normal finally cleanup. The temporary output is removed on normal cleanup when possible.


## Click-validation event race

A local visual E2E run exposed a race in the first event-based Web validation. Step 2 could present its bubble and the user/test could click the target before the next 100ms validation reconciliation installed the clicked listener. That click was then lost, leaving Step 2 active and allowing its bubble to reappear after DOM/context movement. WebBubblePresenter now arms clicked validation on the uniquely resolved target as part of bubble presentation, before the instruction becomes actionable; WebValidationEvaluator continues to consume the recorded state. This fix requires a local E2E rerun before the regression is marked closed. Bubble timing diagnostics now print every ensure-presentation call rather than suppressing calls below 250ms.


## Startup timing harness correction

The 6.4-second Playwright.CreateAsync measurements were recorded after E2E process isolation changed DAP.App to a brand-new GUID-named temporary output directory on every run. Earlier direct-executable measurement from a stable build location was about 2.2 seconds total to first bubble. Because Playwright starts its packaged driver/runtime from the application output, repeatedly copying it to a never-before-used path can distort cold-start measurements (for example through first-use filesystem/security scanning). The E2E now uses a stable isolated `%TEMP%\DAP\E2E\app` output directory: it remains separate from the normal DAP.App bin directory, while the existing owned-process Ctrl+C cleanup protects subsequent builds. The directory is intentionally retained between runs. A local rerun is required before deciding whether Playwright lifecycle architecture needs optimization based on the previous 6.4-second measurements.


## E2E orphan recovery for stable output

The stable E2E DAP output exposed one remaining interruption case: an orphaned DAP.exe can survive a prior parent termination and lock `%TEMP%\DAP\E2E\app` before the next run reaches its own process cleanup. The E2E now performs ownership-scoped recovery before build: it enumerates processes named DAP and terminates a candidate only when its executable path exactly matches the harness-owned `%TEMP%\DAP\E2E\app\DAP.exe`. Other installed/development DAP processes are never terminated by this recovery. Normal finally/process-exit cleanup remains in place as the first line of defense.


## Immediate learner feedback for click validation

For Web Steps using `ValidationDefinition("clicked")`, the browser-side capture listener now removes the active Step bubble immediately when the target is clicked, after recording the click validation state. The Learner Runtime still owns validation completion and ordered Step advancement on its reconciliation loop; immediate removal is presentation feedback only. This prevents a completed `לחץ כאן` instruction from remaining visibly attached to the target during the interval before the next runtime poll or while the host application begins its own server/DOM update. The behavior is generic to clicked validation and contains no TestCRM-specific logic. Local visual verification is required.


## DAP-side event validation state

Web `clicked` validation no longer stores completion in the guided application's DOM. `WebValidationSession` exposes a Playwright page binding (`__dapReportValidation`) and records completed Step IDs in DAP.exe memory. Page bindings are available to frames and survive navigation, so a click can be retained even when the application immediately performs a server round trip and replaces the target iframe/document. `WebBubblePresenter` arms the target capture listener and reports the Step ID through the binding while dismissing the visible instruction immediately. `WebLearnerRuntime` checks DAP-side event completion before Step context evaluation and before presentation; therefore a validating action that itself leaves/replaces the Step context can still complete the Step and cannot cause the old bubble to be recreated. DOM-backed `clicked` polling was removed from `WebValidationEvaluator`. Local visual/E2E verification is required before marking this behavior verified.
