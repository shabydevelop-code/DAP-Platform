# Current Status

## Current verified baseline — 2026-10-05

The canonical persisted Guides contain **55 Steps** on both Web and Windows. Step 55 is the persisted centered Guide summary; existing 54/54 results below are historical verification from before that Step was added.

Current verified execution baseline:
- **Full Guided Fast/Visual is green cross-platform:** Web Fast, Web Visual, Windows Fast, and Windows Visual have each completed the full persisted 54-Step Guide.
- **Windows Production package is verified 54/54 PASS** after republishing the current repository.
- **Windows packaged Visual-From-Step is verified through Step 54.** A packaged-only defect that could fail before DAP launch because the temporary run root did not exist before writing `resume-context.json` was fixed in commit `405c1b4e577ff193be96310b8df25d6b0dc30284`.
- The earlier Windows Production Fast failure at Step 12 did not reproduce after the current package was republished. No Runtime, bubble, resolver, or timeout workaround was introduced for that transient failure.
- This baseline does **not** claim a fresh complete 54-Step matrix for Unguided, Manual, Manual-From-Step, and every focused start value.

Current mode contract:
- `Fast` and `Visual` are E2E presentation modes over the same synthetic learner action path. Visual may add visible cursor travel and presentation pacing; it must not change typing/commit semantics, business actions, readiness rules, validation, scrolling requirements, or progression.
- `ManualFromStep` and `VisualFromStep` use an Unguided bootstrap for Steps before N and start DAP at N with validated resume context.
- The diagnostics launcher accepts exactly: `Fast`, `Visual`, `Manual`, `Unguided`, `ManualFromStep`, and `VisualFromStep`. Names such as `GuidedFast` are not valid.
- The 5-second E2E timeout policy remains unchanged.

### Web steady-state bubble tracking optimization — 2026-10-06

- A stably presented Web Step no longer performs full target resolution and bubble presentation every 100 ms.
- While the target/bubble/context remain valid, browser-side DOM/context observation wakes the Runtime only when presentation becomes invalid or validation completes.
- The existing 100 ms reconciliation interval remains only for transient recovery states such as missing/inactive targets and pending post-click completion conditions.
- Scroll, resize, and target-size changes continue to reposition/hide the normal attached bubble inside the browser without Runtime polling.
- Cross-frame top-level proxy bubbles are reused instead of removed/recreated on every refresh. Their position is refreshed at a lower 250 ms rate only while the proxy is required.
- A cross-frame proxy is hidden when its target leaves the top-level viewport and restored beside the target when it returns; it is no longer clamped on-screen independently of an off-screen target.
- A stale proxy is removed when the child-frame bubble can again be rendered normally.
- No E2E timeout was increased. Full 54-Step regression after this optimization is still pending local execution.

### Web first-bubble startup timing — Production measurement

A packaged Production Web Fast run measured **7217 ms from DAP.exe start to first observed bubble**. Internal DAP instrumentation isolated the dominant cost:
- SQLite initialized: 84 ms.
- Guide loaded: 144 ms.
- Web composition root created: 257 ms.
- `Playwright.CreateAsync()` completed: 6563 ms.
- CDP connection completed: 6670 ms.
- Web Guide Runtime started: 6673 ms.
- First bubble presentation then required 159 ms of active-Step work.

The same Production diagnostic run measured 5613 ms for the E2E runner's own separate Playwright initialization. Inspection of Playwright .NET 1.55.0 confirms that `Playwright.CreateAsync()` creates a stdio transport, starts the packaged Playwright driver process with `run-driver`, and waits for Playwright initialization. Therefore the current Web startup bottleneck is Playwright/driver initialization, not SQLite, Guide loading, CDP connection, target resolution, or bubble rendering.

No speculative Runtime optimization has been committed yet. In particular, the 10-second CDP connection timeout is a maximum timeout and is not the measured fixed startup delay. Any future optimization must remain generic for closed customer Web applications and must not introduce a TestCRM-specific shortcut.

### External UI localization — 2026-10-03

- Added runtime-loaded external localization under `src/DAP.App/Localization`: `language.json`, `he.json`, and `en.json`.
- Build/publish output copies these JSON files beside the compiled application so wording can be changed without recompiling `DAP.exe`.
- Added shared `IUiTextProvider` contract and JSON-backed application implementation.
- Web and Windows learner bubble system text now comes from localization keys, including Step progress and drag-handle tooltip; Web completion text/button also comes from localization.
- DAP application MessageBox text for learner completion, empty Guide, matching-browser-page errors, infrastructure check, and launch usage now comes from external localization.
- UI direction is read from the active language file.
- There is intentionally no RESX/compiled/string fallback. Missing localization configuration, language file, key, or invalid direction is an explicit configuration error.
- Guide bubble instructional content remains persisted Guide data and is not product localization.
- The localization files have since been compiled and loaded successfully by the current Guided startup path. A dedicated Hebrew/English switching and localization-failure regression matrix has not yet been reported as complete.

### Manual learner / UX parity refinements — 2026-10-03

Full human-learner execution is now owned by the same canonical E2E runner used for automated validation:
- Web: `dotnet run --project tests\DAP.TestCRM.Web.E2E\DAP.TestCRM.Web.E2E.csproj -- --manual`
- Windows: `dotnet run --project tests\DAP.TestCRM.Windows.E2E\DAP.TestCRM.Windows.E2E.csproj -- --manual`

The duplicate PowerShell launchers were removed. `--manual` starts the normal TestCRM/DAP topology, waits until production Step 1 is visibly ready, disables synthetic learner actions, and leaves the human learner to complete the full Guide. The same runner owns startup, process cleanup, ports, database wiring, and DAP launch for automated and manual modes. `--manual-from-step <N>` remains the state-preserving focused handoff mode.

Recent parity refinements:
- Web full-manual mode uses the in-browser DAP completion bubble only; the unified Web E2E manual path does not add `--show-completion`, avoiding a duplicate Windows MessageBox.
- **DAP completion-dialog foreground behavior — verified 2026-10-03.** The DAP-owned OS completion dialog used by Windows/manual completion is now explicitly brought to the foreground when shown, so it does not open hidden behind the target CRM/browser. The foreground/topmost request is scoped to the short-lived completion dialog and does not leave persistent Topmost state after dismissal. Manual verification confirmed the alert appears in focus.
- Web and Windows TestCRM grids now force RTL/right-aligned column headers. Sortable Web headers render an explicit active ▲/▼ indicator with `aria-sort`; the Windows Cases status header renders the same visible direction indicator while preserving the stable automation name/id used by the Guide.
- Manual runner lifetime follows the owned target application as well as DAP. Web now uses explicit Playwright page-close/browser-disconnect events rather than polling connection state; an unexpected TestCRM Web-host exit is treated as an error, not as an operator close. Windows exits manual mode when the owned TestCRM process closes. The same target-close rule now applies to automated Guided/Unguided runs: closing the runner-owned Web page/browser or Windows application ends the run cleanly, triggers owned-process cleanup, and returns to the shell instead of surfacing a timeout/stack trace.
- **Operator-close verification — 2026-10-03:** Windows Guided and Web Guided were both manually interrupted by closing the target application/browser. In both cases the E2E console returned cleanly with owned-process cleanup and without an unhandled exception or later timeout. This verifies the target-close path specifically; it is not a replacement for a new full 53/53 regression run.
- Web E2E/manual startup now preflights canonical ports 5200 and 5201 before building/launching. It never kills an arbitrary existing port owner. Web readiness is accepted only while the exact Web-host process launched by the current runner is still alive, so a stale process on 5200 cannot satisfy readiness for a failed launch.
- Windows Case-grid sorting now mirrors Web learner intent: Step 6 targets the `סטטוס` column header in `CasesGrid`; the separate visible "מיין לפי סטטוס" action button was removed.
- Windows text-entry validation now follows natural commit semantics like Web: editing does not advance on the first valid/intermediate character. The learner must edit the field and then move focus away before `value-not-empty` / `value-equals` validation can complete. Discrete controls such as ComboBox continue to commit on selection change. Windows observes text value changes and the target edit control's own `HasKeyboardFocusProperty` transitions through a target-scoped UIA property subscription, with polling retained only as a provider fallback. The Runtime also captures the target focus state when the UIA value-change notification arrives, covering fast edit/blur sequences without a global focus listener. Canonical Windows E2E text actions synchronize on the real UIA `ValuePattern.ValueProperty` notification before issuing a real TAB commit, so the synthetic learner follows the same observable WPF edit -> blur lifecycle as production. This synchronization restored the full Windows Guided 53/53 regression at Step 8 without increasing the 5-second timeout.
- Windows initial Step presentation now attempts one-time automatic viewport adjustment through production-observable UIA scrolling. Targets that are clipped or too close to a scroll viewport edge are brought toward a comfortable central region before bubble placement. Reconciliation does not continuously re-center the target, so intentional learner scrolling remains authoritative after initial presentation.
- Windows manual bubble dragging hides the directional pointer immediately when dragging begins and keeps it hidden for the remainder of that active Step. The next Step resets to automatic placement and restores its pointer.
- Web reconciliation trace noise was reduced by removing repetitive successful context/EnsureShown lines while retaining meaningful state/race diagnostics.
- Windows Step timing now logs the first bubble presentation once per active Step instead of logging every reconciliation refresh.
- Windows Guided/Manual E2E no longer builds/runs DAP from `src/DAP.App/bin/Debug`. Backend, Windows TestCRM, and DAP are built into a unique per-run directory under `%TEMP%\DAP\E2E\Windows\<run-id>`, preventing interrupted learner runs from locking normal repository build outputs.
- Windows E2E also registers process-exit/Ctrl+C cleanup for the child processes it owns. Hard termination may still leave a temporary run directory, but later runs never reuse it.
- Web Guided/Manual/Unguided E2E now follows the same isolation rule: TestCRM Server, TestCRM Web, and (when applicable) DAP are built into a unique `%TEMP%\DAP\E2E\Web\<run-id>` tree and launched from there. Process-exit/Ctrl+C cleanup is registered before child startup, so an interrupted run cannot lock the repository's normal Web/TestCRM/DAP build outputs.

- Web non-click validation events are now commit attempts, not permanently sticky Step completion. An invalid blur/change attempt is consumed; later typing cannot advance until a new natural commit event occurs. Web listener edit state is preserved across reconciliation and reset after each commit attempt.
- Windows text validation follows the same rule: an invalid blur consumes the current text commit and establishes a fresh value baseline. Refocusing and completing the correct value while still in the field does not advance until focus leaves again.
- Web already contains the Step-1 regression sequence for invalid commit -> refocus -> exact value without blur -> still Step 1 -> blur -> Step 2. Windows Guided E2E now contains the equivalent regression sequence.

The four-mode 53/53 PASS matrix above remains the shared cross-platform baseline. In addition, Windows Guided was re-run after the latest Windows text-commit fixes and again passed all 53/53 Steps on 2026-10-03. This newer Windows-only rerun does not by itself constitute a fresh four-mode regression of Web Guided/Unguided and Windows Unguided.

Last updated: 2026-10-03

## Closed-target / black-box architectural rule — 2026-10-02

DAP is required to support closed third-party target applications. Production Runtime and Instructor/Editor behavior must not depend on access to target source code, internal databases, private APIs, or implementation details unavailable through DAP's production-observable interfaces. During development, TestCRM source may be inspected for diagnosis and learning: to understand why observed UI/runtime behavior occurs, study patterns that may also appear in closed applications, and distinguish fixture defects from generic DAP limitations. **Source inspection is allowed for diagnosis and learning; it is not allowed as a resolver/runtime oracle.** Any resulting fix must be generic, must rely only on production-observable interfaces at runtime, and must continue to work when the target is a black box. TestCRM is intentionally used to surface difficult black-box targeting/runtime conditions early, and inability to discover sufficient target information through production interfaces is treated as a DAP product capability gap rather than bypassed with source knowledge.

## Historical verified baseline — 2026-10-02

- The repository seed for `testcrm-web-canonical-workflow` now defines 54 Steps. A previously persisted `DAP.db` may still contain 53 until `--reset-guide` is run.
- Normal Web E2E is locally verified PASS for the full 53-Step Customer -> Site -> Case -> Lead workflow using `DAP.exe`, the production Web Runtime, real bubbles, validation, and persisted Guide data.
- Web Unguided is locally verified PASS for the same canonical 53-Step workflow sequenced by the same persisted Guide, with `DAP.exe` and bubble synchronization intentionally omitted.
- Unguided is not a separate TestCRM QA workflow. It is the same canonical business scenario executed without the learner presentation/runtime process.
- Repeated synchronization on the currently active Guide Step is valid when the harness first waits for a transition and then waits again immediately before that Step's learner action; the sequence guard rejects backward movement and skipped Steps.
- Temporary Step-2/backend diagnostics used during stabilization have been removed after both modes passed.
- The 5-second E2E timeout rule remains unchanged.

### Windows production-runtime milestone

- `src/DAP.Runtime.Windows` now exists as production code using Microsoft UI Automation.
- It includes production target resolution, WPF learner bubble presentation, Windows validation, and ordered Windows Guide execution.
- `DAP.exe --learner-windows <guide-key> --window-automation-id <id>` loads persisted Guide data and runs Windows Steps.
- The repository seed for `testcrm-windows-canonical-workflow` now defines 54 production-runtime Steps. A previously persisted Guide remains 53 until explicitly reset.
- All 53 persisted Windows Steps are locally verified PASS in both Windows Guided and Windows Unguided execution; Guided uses the real DAP Windows Learner Runtime, UIA targets, runtime capture, modal targeting, completion conditions, and bubbles.
- Windows Unguided executes the same persisted 53-Step canonical Guide without DAP Runtime/bubbles; it is the current replacement for the historical Unguided wording.
- Windows persisted-Guide coverage is complete at 53/53. Instructor/Picker work can proceed without treating Learner Runtime coverage expansion as an outstanding prerequisite.

## Historical phase snapshot

At this 2026-10-02 milestone, Web and Windows canonical 53-Step execution was stable in Guided and Unguided modes. This is historical context; the current Guide and Guided Fast/Visual baseline are 54 Steps as documented above.

## Current E2E baseline

- `tests/DAP.TestCRM.E2E.Common/CanonicalCrmScenario.cs` contains the shared representative Customer -> Site -> Case -> Lead workflow used by the platform-specific Web and Windows E2E runners.
- The workflow currently passes end-to-end, including ten validated business-facing scenarios, dynamic Lead deletion, and Case deletion.
- Playwright default timeout is 5 seconds for the E2E runner.
- Canonical runner-mode vocabulary is intentionally limited to `fast|visual`; `demo` has been removed and must not be accepted as an alias. Web/Windows runner mode names and user-facing semantics are now an explicit parity contract:
- E2E execution modes:
  - `fast` (default): skips artificial human/visual delays and TestCRM's artificial server-thinking delay.
  - `visual`: preserves the same learner actions as Fast while adding visible cursor movement and presentation pacing; it may retain presentation-oriented processing feedback/artificial fixture delay, but must not change typing or commit semantics.
- Any future runner-mode/CLI change must be reviewed for both Web and Windows in the same change; intentional platform-only behavior requires an explicit documented exception.
- Real readiness conditions remain active in both modes. The E2E does not replace actual server/DOM/frame readiness with fixed sleeps.
- PeopleSoft-style Content iframe replacement is handled by re-resolving the active frame and waiting for real route readiness.
- Scenario coverage now includes Case FieldChange + iframe replacement, server validation with unsaved-value preservation, Grid rerender/reorder + target re-resolution, Content-document reload with preserved Case context, CRM tab switching with preserved business context, conditional target disappearance/reappearance with re-resolution, cross-frame Header-to-Content navigation, Layout Shift + target re-resolution, consecutive server updates with final-state re-resolution, and business-context switching with target isolation.
- The E2E scenarios use real UI/application behavior; no TestCRM-specific route-persistence workaround is used for the validated baseline.
- The permanent TestCRM server still exposes the `מעבד...` activity indicator and artificial server delay in normal/visual behavior; the E2E fast mode bypasses those artificial delays only for test execution.
- Historical implementation note: frame polling originally remained at 100 ms. Current stable Web presentation is event-driven; the 100 ms reconciliation cadence is retained only for transient recovery states and short-lived pending completion conditions, with cross-frame proxy geometry refreshed at 250 ms while required.
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
- Full end-user Learner shell/catalog UI beyond the current runtime-driven bubble flow.
- Editor UI.
- Recorder.
- Production deployment/bootstrap validation.


## Current implementation scope

Architecture/Core scope and active runtime implementation both cover Web + Windows. The current canonical repository Guides contain 54 Steps, and full Guided Fast/Visual is verified 54/54 on both platforms. The earlier 53-Step Guided/Unguided results remain historical baselines. Windows has production UIA target resolution, WPF bubble presentation, validation, runtime capture, modal targeting, ordered Guide execution, persisted completion conditions, and a locally verified full 54-Step Guided Learner path.

## Persistence foundation
SQLite default database location on Windows is `%ProgramData%\DAP\Data\DAP.db` (normally `C:\ProgramData\DAP\Data\DAP.db`). The application/provider may override this with `DAP_DATABASE_PATH`; Core must not depend on either the path or SQLite.


The shared Core now includes initial runtime-neutral `ValidationDefinition`, `BubbleDefinition`, and `GuideStep` models. `DAP.Data` defines the guide-step repository abstraction. `DAP.Data.Sqlite` now contains the SQLite connection/bootstrap implementation, schema initialization, and `SqliteGuideStepRepository` with transactional save/load mapping for TargetDescriptor, ordered multiple Anchors, FrameContext, Bubble, Validation, and Step advance mode. Core remains independent from SQLite. A standalone `DAP.Data.Sqlite.Tests` round-trip executable uses a temporary database and does not contain production persistence implementation.

The schema is a provider implementation detail, not the domain contract. Active Runtime implementation covers both Web and Windows through shared Core contracts and runtime-specific adapters.

## First Web bubble presenter

Production `DAP.Runtime.Web` now contains the first `WebBubblePresenter`. It resolves a GuideStep target through `WebTargetResolver`, injects the bubble into the resolved target document/frame, and keeps it positioned on scroll/resize. It does not live in the E2E project. A TestCRM-specific first-step definition exists only as an E2E fixture and is intentionally excluded from production Data/Runtime code.

Bubble visual styling is now separated from GuideStep persistence: `WebBubbleTheme` owns the Web presentation defaults (colors, typography, border, radius, spacing, shadow), while persisted `BubbleDefinition` continues to carry only step-specific content and placement. `WebBubblePresenter` consumes the theme and cleans up prior bubble observers/listeners before replacing a bubble in the same document. No per-step colors or CSS were added to SQLite.

The Web bubble now makes target association explicit: `WebBubblePresenter` highlights the resolved DOM element itself using runtime-applied outline/shadow styling and renders a directional pointer on the bubble. It does not use a separate target-sized overlay, avoiding box-model/border alignment errors. Highlight/pointer appearance is owned by `WebBubbleTheme`, not persisted per Step. The presenter's cleanup restores the target element's prior inline outline/outline-offset/box-shadow values and removes its observers/listeners.

The TestCRM E2E harness is now wired to the production `DAP.Data.Sqlite` and `DAP.Runtime.Web` projects. Before the existing CRM workflow starts, it initializes a temporary DAP SQLite database, persists the first TestCRM GuideStep, reloads it through `SqliteGuideStepRepository`, resolves its real target through `WebTargetResolver`, presents it through `WebBubblePresenter`, and asserts the rendered bubble content. The fixture definition remains test-only; the persistence and bubble implementation are production code.


The TestCRM E2E now starts the production `WebLearnerRuntime` for the SQLite-loaded first Step, verifies its initial bubble, explicitly reloads the active Content document, and then requires the runtime to restore the same Step bubble on the newly resolved `[name='name']` target without any test-side call to `WebBubblePresenter`. This is the first direct E2E assertion of the production lifecycle across document replacement. This lifecycle behavior is covered by the current passing regression baseline.

`DAP.Runtime.Web.Learner.WebLearnerRuntime` owns the production active-Step lifecycle. Target resolution remains descriptor-driven and recovery still re-resolves after iframe/document replacement, rerender, ambiguity, or context loss. Stable presentation is now event-driven: once a unique target and bubble are valid, browser-side observation and validation events wake the Runtime instead of full 100 ms re-resolution. The 100 ms interval remains a recovery cadence for transient missing/inactive states and short-lived pending completion conditions. Cross-frame top-level proxies use a lower 250 ms refresh only while such a proxy is required. `WebValidationEvaluator` continues to evaluate runtime-specific Web validation, and Manual Steps are not auto-completed.

A runtime-neutral `StepContextDefinition` is now part of `GuideStep` and is persisted by SQLite. The Web adapter implements `WebStepContextGuard`; initial supported Web context predicates are `url-equals`, `url-contains`, `url-fragment-equals`, and `css-exists`, evaluated against the live frame identified by the Step's `FrameContext`. `WebLearnerRuntime` checks this guard before target resolution. This allows iframe/document replacement within the same logical route while suppressing the active Step after the user leaves that business context. The TestCRM customer-search fixture uses the stable screen marker `#customer-search` rather than coupling the Step to a route fragment, and E2E now asserts that the bubble disappears after navigation leaves customer search. These behaviors are covered by the current passing regression baseline.

## Historical milestone — initial Core/Web resolver phase

At this earlier milestone, the runtime-neutral target-resolution Core had just been started in `src/DAP.Core`. It defines `TargetDescriptor`, `Locator`, `Anchor`, `FrameContext`, runtime identity, and explicit `Resolved` / `NotFound` / `Ambiguous` resolution results. The Core has no Playwright, UIA, SQLite, or test-project dependency.

The target-resolver contract now exists in `DAP.Core`, and the initial Playwright-backed `WebTargetResolver` exists in `DAP.Runtime.Web`. The Web implementation re-resolves frame hierarchy per resolution request, filters candidates through all configured anchors, and returns explicit `NotFound` / `Resolved` / `Ambiguous` outcomes. Playwright remains outside Core. Initial anchor-relation evaluation currently supports CSS anchors; this is a deliberate first implementation boundary, not a Core limitation.

Historical next-step note: focused resolver tests, shared `Validation`/`Bubble`/`GuideStep` models, and Learner Runtime wiring were still pending here. Those foundations have since been implemented; this paragraph is retained only as development history.


### Validation coverage
The validated E2E baseline already includes a real CRM value-validation flow in the Case workflow: changing status to `סגורה` makes `closeReason` required; an attempted save without that value is rejected by server validation while unsaved Subject and Description values are preserved; supplying `closeReason` (`טופל`) then allows the save to succeed. This validation currently runs without DAP bubbles and is part of the underlying CRM/E2E behavior, not a bubble-specific test.


### Target resolution model
A target is represented by a runtime-neutral TargetDescriptor rather than a single selector. It identifies the target through a primary locator plus zero or more anchors/context constraints. Resolution must discover candidates, apply the anchors, verify uniqueness, and return an explicit ambiguous/not-found result rather than guessing. The descriptor also carries the runtime and frame context required by the corresponding Web or Windows adapter. This model is intended to support re-resolution after DOM changes, iframe replacement, grid rerender/reorder, layout shifts, target disappearance/reappearance, and equivalent Windows UI changes.


## Historical milestone — initial DAP executable host

At this earlier milestone, `src/DAP.App` had just become as a real `net8.0-windows` WPF `WinExe` with assembly name `DAP`. It references Core, Data, SQLite, and the Web Runtime and acts as the production composition root. `--check` initializes the configured SQLite provider, composes the Web Learner runtime services, writes `%TEMP%\\DAP\\dap-check.txt`, and displays a WPF confirmation dialog so infrastructure-check success is visible even though DAP is a `WinExe`. `--learner-web <guide-id> --cdp <endpoint> [--page-url-contains <text>]` now implements the first independent Web attachment path through Playwright `ConnectOverCDPAsync`: it requires exactly one matching Chromium page, loads the persisted guide Steps, and runs the first Step from inside `DAP.exe`. At that milestone this path still required local build/runtime validation. The then-current E2E harness launched Chromium with a per-run CDP port, persisted a fixture Step into a temporary SQLite database, started `DAP.App` through a separate `dotnet` process with that database path and CDP endpoint, and expected the external DAP process to own bubble re-resolution/context behavior. The former in-process `WebLearnerRuntime` wiring has been removed from this E2E path. The E2E continues to host the Learner runtime in-process only as behavioral regression coverage. No separate Bubble.exe is planned; bubble lifecycle belongs to DAP.exe.


## Automatic Web Step validation

The first production validation path is now implemented. `ValidationDefinition` remains runtime-neutral in Core; `WebValidationEvaluator` owns Web interpretation. The external-process TestCRM E2E now fills the customer-name target and requires the active bubble to disappear from successful `value-not-empty` validation before CRM navigation changes logical context. It then verifies that the completed Step does not reappear after route navigation. These validation assertions are covered by the current passing regression baseline.


## Ordered Web guide lifecycle

`WebGuideRuntime` now owns ordered guide orchestration while `WebLearnerRuntime` remains responsible for one active Step. `DAP.exe` passes the complete persisted Step list to the guide runtime instead of running only `steps[0]`. The TestCRM fixture now contains two persisted Steps: customer-name (`value-not-empty`) followed by customer-search-button (`clicked`). The E2E requires the second bubble to appear after Step 1 completes. The two-Step transition is locally verified passing: Step 1 automatic validation advances to Step 2, and the completed Step remains inactive after route change.


## Direct DAP.exe E2E startup

The TestCRM E2E now separates build time from runtime startup. It builds `DAP.App` explicitly, then launches the resulting `DAP.exe` directly instead of using `dotnet run` as the learner process. The test reports elapsed time from starting the executable until the first production bubble is observed. This matches the deployed product process boundary more closely and prevents MSBuild time from being mistaken for DAP runtime startup latency. Local timing is now measured. A representative visual run reported 7648 ms from DAP.exe process start until the first bubble was observed. Internal instrumentation isolated the dominant startup cost: SQLite initialization completed at 233 ms, composition at 234 ms, Playwright.CreateAsync completed at 6628 ms, CDP connection at 6731 ms, page selection at 6733 ms, guide load at 6749 ms, and guide runtime start at 6750 ms. Thus roughly 6.4 seconds of that run were spent inside Playwright.CreateAsync, while CDP connection and guide persistence were comparatively small. Bubble target-resolution/DOM-presentation instrumentation has been added next so the remaining post-guide-start interval can be separated from E2E observation latency before optimization.


## Interrupted E2E process isolation

The TestCRM E2E now builds DAP.App into a unique temporary E2E-owned output directory for each run instead of the project's normal bin directory. This prevents an orphaned DAP.exe from an interrupted run from locking the next build. The E2E also registers a process-exit cleanup safety net that terminates only the DAP process tree created by that test run, covering Ctrl+C/process termination paths in addition to the normal finally cleanup. The temporary output is removed on normal cleanup when possible.


## Click-validation event race

A local visual E2E run exposed a race in the first event-based Web validation. Step 2 could present its bubble and the user/test could click the target before the next 100ms validation reconciliation installed the clicked listener. That click was then lost, leaving Step 2 active and allowing its bubble to reappear after DOM/context movement. WebBubblePresenter now arms clicked validation on the uniquely resolved target as part of bubble presentation, before the instruction becomes actionable; WebValidationEvaluator continues to consume the recorded state. Historical verification note: this fix initially required a local rerun. Later full Guided/Visual regression runs supersede that pending verification state.


## Historical milestone — startup timing harness correction

The 6.4-second Playwright.CreateAsync measurements were recorded after E2E process isolation changed DAP.App to a brand-new GUID-named temporary output directory on every run. Earlier direct-executable measurement from a stable build location was about 2.2 seconds total to first bubble. Because Playwright starts its packaged driver/runtime from the application output, repeatedly copying it to a never-before-used path can distort cold-start measurements (for example through first-use filesystem/security scanning). At that historical point the E2E temporarily moved to a stable isolated `%TEMP%\DAP\E2E\app` output directory. That strategy has since been superseded: current Web and Windows canonical runners build into a unique GUID-based per-run root under `%TEMP%\DAP\E2E\Web\<run-id>` or `%TEMP%\DAP\E2E\Windows\<run-id>` and never reuse an abandoned executable path.


## Historical milestone — orphan recovery for the former stable output

The former stable E2E DAP output exposed an orphan-lock problem and briefly used ownership-scoped recovery for `%TEMP%\DAP\E2E\app\DAP.exe`. That mechanism belongs to the superseded stable-output design. Current canonical runners use unique per-run executable roots, so later runs do not reuse or kill an abandoned DAP executable path.


## Immediate learner feedback for click validation

For Web Steps using `ValidationDefinition("clicked")`, the browser-side capture listener now removes the active Step bubble immediately when the target is clicked, after recording the click validation state. The Learner Runtime still owns validation completion and ordered Step advancement on its reconciliation loop; immediate removal is presentation feedback only. This prevents a completed `לחץ כאן` instruction from remaining visibly attached to the target during the interval before the next runtime poll or while the host application begins its own server/DOM update. The behavior is generic to clicked validation and contains no TestCRM-specific logic. Later full Guided/Visual runs exercise this path; the earlier pending local-verification state is closed.


## DAP-side event validation state

Web `clicked` validation no longer stores completion in the guided application's DOM. `WebValidationSession` exposes a Playwright page binding (`__dapReportValidation`) and records completed Step IDs in DAP.exe memory. Page bindings are available to frames and survive navigation, so a click can be retained even when the application immediately performs a server round trip and replaces the target iframe/document. `WebBubblePresenter` arms the target capture listener and reports the Step ID through the binding while dismissing the visible instruction immediately. `WebLearnerRuntime` checks DAP-side event completion before Step context evaluation and before presentation; therefore a validating action that itself leaves/replaces the Step context can still complete the Step and cannot cause the old bubble to be recreated. DOM-backed `clicked` polling was removed from `WebValidationEvaluator`. Later full Guided/Visual regression runs exercise the event-backed click path; the earlier pending verification state is closed.


## Historical milestone — first complete 9-Step TestCRM learner segment
- At this earlier milestone, the first production-backed Web Guide spanned one complete business segment instead of only the initial search.
- Guide: customer search -> open customer -> open first site -> Cases tab -> create Case -> enter subject -> enter description -> save.
- The Guide contains 9 persisted Steps using production Core contracts and the production Learner Runtime.
- The E2E runner waits for the relevant production bubble before each guided action; it does not create or advance bubbles itself.
- The same Guide is therefore suitable for a future/manual learner run: automation is only acting as the learner.
- Existing broader CRM resilience scenarios continue after the Guide completes and remain independent of an active Guide.
- No TestCRM-specific validation kind was added; this flow is covered by the generic `clicked` and `value-not-empty` validations already owned by the Web runtime.




## Historical milestone — early visual demo direction
- At this earlier milestone the target was a polished visual TestCRM demonstration rather than a separate manual-run harness.
- The persisted Guide contained 9 Steps at that time; it has since expanded to the canonical 53-Step workflow.
- Next bubble work should extend the visual demo only where guidance is meaningful, especially across real CRM server/DOM transitions, validation and business-state changes; avoid adding bubbles merely for test coverage.


## Demo Guide/action synchronization
- Visual-mode invariant: while a Guide Step is active, the next visible learner action performed by the E2E must be exactly the action instructed by that Step.
- Found and fixed a concrete mismatch after entering the Cases tab: E2E sorted the grid while the active next bubble instructed the learner to create a Case.
- Cases status sort is now its own persisted clicked Step. The initial guided segment is now 10 Steps.
- The E2E explicitly waits for Step 6 before sorting, then Steps 7-10 before create/fill/fill/save.
- Visible business actions must not be inserted between guided Steps without either a corresponding Step or an explicit decision that the Guide segment has ended.


## Demo Guide extended through Case treatment
- The visual Guide no longer ends immediately after saving the new Case.
- Steps 11-14 now guide: return to Cases grid -> open the created Case -> change status to In Progress -> enter treatment notes.
- This specifically covers the previously unguided area around the Cases grid/Open action and makes the active bubble/action relationship observable.
- No second Cases sort click exists in this segment; the only explicit Cases status sort remains guided earlier.
- The E2E keeps its exact created-Case selector for deterministic assertions, while the persisted Step targets the visible Open action in the Cases business context.


## Step 12 ambiguity / exact-value validation fix
- Visual run exposed Step 12 timeout after returning to the Cases grid.
- Root cause: persisted target `button.grid-open` matched multiple Case rows; production resolver correctly returned Ambiguous and refused to guess.
- Step 12 now scopes the Open button to the row containing the known Case subject `תקלה בחיבור לאינטרנט`, making the target unique without relying on row order.
- Also found Step 13 could auto-complete incorrectly because `value-not-empty` is already true for the initial status `פתוחה`.
- Added generic Web validation kind `value-equals` using persisted `ValidationExpectedValue`; Step 13 now completes only when status equals `בטיפול`.


## Step 12 deterministic business target + bubble first-frame fix
- Live diagnostics showed 61 Case rows and 60 rows sharing the old subject `תקלה בחיבור לאינטרנט`; subject-only Step 12 resolution was therefore correctly Ambiguous.
- The visual E2E now creates a unique Case subject before seeding the Guide. The same run-specific business value is entered in Step 8 and persisted into Step 12's row-scoped target, so the created Case can be identified without generated database IDs or row-order assumptions.
- The temporary Step 12 row-by-row Playwright diagnostics were removed after identifying the ambiguity; they must not remain in the visual E2E because the repeated cross-process DOM calls noticeably degrade demo responsiveness.
- A separate visual defect was confirmed in the presenter path: a newly appended bubble could paint once before `place()` assigned coordinates, appearing briefly at the top-left.
- New Web bubbles now start with `visibility:hidden` and become visible only after placement and pointer calculation complete.


## Visual Guide pacing
- Verified that Step 14 (`testcrm-resolution-notes`) was not skipped: `WaitForGuideStep(14)` only returns after the exact Step 14 bubble text exists in the live Content frame.
- The visual harness previously began `Fill()` immediately after observing a new bubble, making short-lived instructions difficult to perceive before cursor movement/scrolling started.
- In visual mode, `WaitForGuideStep` now gives every newly observed Guide instruction a 500 ms presentation beat before the learner action begins. Fast mode is unchanged.
- This is E2E presentation pacing, not a production Learner Runtime delay.


## Guided Case closure extension
- The visual TestCRM Guide now continues beyond treatment notes through Step 18.
- Step 15 guides the real off-screen Activity More action. Because production bubbles remain hidden for off-screen targets, the visual learner scrolls first, then the E2E requires the Step 15 bubble to be visible before clicking.
- Step 16 requires Case status = Closed, Step 17 guides restoring Subject after the CRM FieldChange clears it, and Step 18 requires Close Reason = Resolved.
- The intentionally rejected Save with missing Close Reason remains an unguided validation exercise for now. A click-only Step would advance incorrectly on the rejected save; final Save guidance is deferred until the Guide can validate successful persistence rather than click occurrence.


## Interaction-completion semantics
- Automatic value Steps no longer advance merely because 100 ms polling observes an intermediate valid value.
- Web controls now report a natural interaction-completion event to the existing DAP.exe validation session.
- Text input/textarea: the learner must actually edit the control and then leave it (blur); only then is the validation condition evaluated.
- Select, checkbox, radio and other discrete value controls: completion is reported on change, then the validation condition is evaluated.
- Button/click Steps retain event-based clicked completion.
- The validation condition and the interaction-completion event are now conceptually separate: the event decides when to evaluate; ValidationDefinition decides whether the completed interaction is acceptable.
- This prevents a free-text Step such as treatment notes from advancing after the first character and is a prerequisite for safe viewport/navigation behavior between Steps.
- The current implementation derives the Web completion event from the resolved control type so existing persisted Guides and SQLite schema remain compatible. A future explicit per-Step completion-trigger override can be added with schema migration/versioning if product requirements need it.


## Non-overlapping Web bubble placement
- Web bubble placement now evaluates Bottom, Top, Right and Left against both the viewport and the actionable target rectangle.
- A requested placement remains the first preference, but the presenter can choose another side when the preferred side would not fit.
- Clamping is restricted to the axis parallel to the target; it no longer slides a bubble across the target-facing axis and over the control.
- Fully visible, non-overlapping candidates are preferred. If none fully fit, the least-overflow non-overlapping candidate is used.
- If no side can avoid covering the actionable target, the bubble remains hidden rather than blocking the required learner action.


## Guided expected validation alert
- The Case closure Guide now has 20 Steps.
- Step 18 explicitly instructs the learner to attempt the close save.
- The intentionally rejected server save produces the real TestCRM `#ps-alert`; Step 19 targets its confirmation button and instructs the learner to acknowledge the error.
- Step 20 then instructs selection of the required Close Reason (`טופל`).
- The E2E no longer dismisses this visible validation alert inside a helper. It waits for the production Guide instruction before clicking the alert confirmation.
- This preserves the visual-guide invariant: while a Guide Step is active, visible learner actions are the actions instructed by that Step.
- This is an expected business-validation branch, not a generic rule that every unexpected application error must automatically become a Guide Step.


## Full visual TestCRM Guide coverage
- The persisted TestCRM Guide now contains 53 ordered Steps and continues from the Case flow through the remainder of the visible E2E workflow.
- Guided visible actions now include: final Case save; Site/Case/Lead tab navigation; Lead creation; contact entry; Lead save; repeated server-driven Lead status changes; selected-service validation failure; validation alert acknowledgement; service selection; Lead deletion and confirmation; breadcrumb navigation; re-entry into Site/Leads; layout-shift status transitions; consecutive status transitions; business-context switch into Cases; Case deletion and confirmation; and the final Header-frame navigation back to customer search.
- Assertions, DOM measurements, readiness waits and the deliberate programmatic Content-document reload remain E2E mechanics and are not represented as learner Steps.
- The E2E waits for each corresponding Guide Step before each visible learner action in the guided sequence.
- `WaitForGuideStep` now searches all live page frames so the final Header-frame bubble can be observed without assuming all bubbles belong to the Content iframe.
- Guide continuation after Close Reason no longer asserts that the bubble disappears at Step 20; Step 21 is the guided successful save.
- Historical note: at this point the full 53-Step Visual run had not yet been re-executed. Later sections record successful full Visual validation.


## Verified full 53-Step visual run
- Local visual E2E execution was completed successfully after the full Guide expansion.
- DAP.exe loaded all 53 persisted Steps and the representative Customer -> Site -> Case -> Lead workflow completed with no exception or timeout.
- Verified in the successful run: automatic validation completion, Step 1 -> Step 2 transition, complete Case treatment, expected validation alert acknowledgement, Case closure, Lead workflow, dynamic Lead deletion, Case deletion, business-context transitions and final cross-frame navigation.
- All visible learner actions in the current representative E2E flow are now Guide-driven; assertions, readiness checks, DOM measurements and deliberate programmatic reload mechanics remain test-only.
- The obsolete `SaveExpectValidationUnguided` helper was removed after full Guide coverage made it unused.


## Learner-movable Web bubbles
- The learner can now drag the active Web bubble with pointer/mouse input when it obscures useful application content.
- Dragging is transient learner UI state only; it is not persisted in SQLite and does not change the authored Step placement.
- Once the learner manually moves a bubble, automatic placement is suspended for the remainder of that Step.
- The manually moved bubble is clamped to the current viewport and remains clamped after scroll/resize.
- The directional pointer is hidden after manual movement so it cannot visually claim an incorrect target direction.
- Cursor feedback uses `grab` / `grabbing`.
- Moving to another Step recreates the bubble and resets manual positioning, returning the new Step to normal automatic placement.
- Pointer dragging ignores interactive descendants so future controls inside a bubble remain usable.
- The existing target highlight, validation lifecycle, target re-resolution and non-overlap auto-placement remain unchanged before manual movement.
- The verified 53-Step baseline predates this drag enhancement; rerun the visual E2E after pulling to regression-check the new presenter behavior.


## DAP bubble visual identity
- The default Web bubble palette now uses a DAP-owned indigo/lavender identity instead of the previous generic enterprise blue.
- Central theme tokens: bubble background `#312E5A`, white text `#FFFFFF`, muted lavender border `#8B83C7`, target highlight `#A99FE8`, with matching restrained shadow/highlight alpha values.
- The palette remains centralized in `WebBubbleTheme.Default`; no Guide Step stores presentation colors and the host application's colors do not influence DAP guidance.
- Placement, dragging, validation, target highlighting mechanics and the 53-Step Guide structure are unchanged.


## Web Step presentation settling
- A newly active Web Step is no longer presented merely because its selector can already resolve during a server-driven render.
- Before presentation, `WebLearnerRuntime` now samples the next target across a short settling window (default 250 ms).
- The Step is eligible for presentation only when resolution remains unique, the same DOM node survives the window, the node remains connected/non-zero, and its bounding geometry remains stable within a small tolerance.
- DOM/frame replacement or layout movement during the settling window causes presentation to remain hidden and the normal reconciliation loop retries from a fresh resolution.
- This is a generic Web Runtime rule, not TestCRM/PeopleSoft-specific logic and not a fixed post-action sleep.
- Existing bubbles continue to use the normal reconciliation path; validation, context guards, target ambiguity rules, dragging and placement semantics are unchanged.
- Historical note: this settling change initially awaited a rerun; later full Visual regression results supersede that pending state.


## Settling regression fix
- The first implementation of Web presentation settling caused the first bubble to time out because it attempted DOM identity comparison by passing an `ElementHandle` as an ordinary `EvaluateAsync` argument.
- The settling check now keeps the first resolved Locator and re-evaluates that original node after the settling window. A replaced/detached node fails through `isConnected` (or transient Playwright failure), while a fresh second resolution independently confirms the uniquely resolved target and stable geometry.
- Geometry is compared across the original node before/after the settling window and against the fresh resolution.
- No E2E timeout was increased; the fix addresses the settling implementation itself.
- Historical note: this regression fix initially awaited a local Visual rerun; later Visual PASS results supersede that pending state.


## Transition settling correction
- The prior target-identity settling approach was removed. Playwright `ILocator` is a live query and must not be treated as a frozen DOM-node identity token.
- The first Guide Step now bypasses transition settling because there is no preceding learner action/server transition to wait for.
- Subsequent Web Steps resolve their target and observe that target document for a quiet DOM window (default 250 ms). Subtree, child-list, attribute or text mutations reset the quiet timer.
- The quiet-window rule is independent of network activity: a local JavaScript rerender and a server-backed rerender are treated the same, while a genuinely quiet transition continues after the short window.
- At the end of the quiet window the target must still be connected and have non-zero geometry.
- This replaces both failed settling implementations that caused the first bubble to time out.
- Historical note: a Visual rerun was still pending at this point; later PASS results supersede this requirement.


## One-time Step presentation gate
- DOM settling is now a one-time gate before the first visible presentation of each Web Step, rather than a condition re-evaluated on every 100 ms reconciliation cycle.
- Step 1 bypasses the gate as before. Each subsequent Step waits for its quiet DOM window while no new bubble is created.
- Once a Step is successfully presented, normal `EnsureShownAsync` reconciliation owns it for the remainder of that Step; later rerenders no longer force the already-active Step back through settling.
- The settling-wait path no longer calls `HideAsync` repeatedly, eliminating the show/hide churn that amplified bubble flicker during rendering.
- If the target disappears in the small gap between passing the gate and its first presentation, the gate is reset and must pass again.
- Historical note: server-render transition observation was still pending at this point; later full Visual PASS results supersede this requirement.


## Guide/E2E target synchronization near end of visual flow
- A concrete Guide/E2E mismatch was found for the created Case: Steps 12 and 50 pointed the bubble at the first Case row, while the E2E visibly clicked the exact Case created during the run.
- TestCRM Case grid buttons now expose a semantic `data-business-subject` attribute in addition to their dynamic business ID.
- Steps 12 and 50 now target the created Case by the known business subject (`תקלה בחיבור לאינטרנט`) rather than by row position.
- The corresponding E2E clicks now use exactly the same semantic selector as the Guide. Before each click, the E2E asserts that the semantic target is unique and that its `data-business-id` equals the dynamically captured `createdCaseId`.
- This preserves deterministic business-record verification without embedding a runtime-generated ID into the persisted Guide and removes the known case where the bubble arrow and visual E2E cursor could point at different Case rows.
- The remaining late-flow Steps were reviewed against their visible E2E actions; Steps 41 and 48 intentionally use the first row on both sides, while breadcrumb/tab/delete/confirm/Header actions resolve the same logical controls.
- Historical note: this late-flow review initially requested another Visual rerun. Later full Visual PASS results supersede that pending check.


## Full Guide/E2E compatibility audit
- All 53 persisted TestCRM Guide Steps were reviewed against the visible E2E workflow, including instruction intent, target selector, validation kind/value, context guard, frame and corresponding learner action.
- A second class of late-flow mismatch was found: several technical E2E assertions moved the visual cursor to re-resolved targets even though those cursor moves were not learner actions and had no Guide Step. The unguided moves to the Lead Delete target, selected-service field and switched-Case status field were removed; the underlying assertions remain non-visual.
- Direct visible actions that bypassed the common learner-action helpers were normalized: Step 41 now opens the first Lead through `Click`, Step 45 changes status through `Select`, and Step 49 returns through the same Site breadcrumb selector used by the Guide.
- `MoveTo` now enforces a runtime synchronization invariant before every visible learner action: the action target must be the exact DOM element stored as `__dapTarget` by the currently active production bubble. A bubble pointing to element A while the E2E cursor acts on element B now fails immediately instead of producing a misleading visual demo.
- This identity check works inside the target's own document, so it also covers Content-frame actions and the final Header-frame action.
- Together with the semantic created-Case target check, the visual E2E now verifies both business-record identity where required and exact bubble/action DOM identity for visible learner actions.
- The audit found no reason to turn technical assertions, measurements, readiness checks or the deliberate programmatic reload into Guide Steps; they remain non-visual E2E mechanics.
- Historical note: the exact DOM-identity invariant initially awaited a full Visual rerun; later full Visual PASS coverage supersedes this pending requirement.


## Step 12 ambiguity found by full compatibility run
- The first local run after the compatibility audit timed out at Step 12. The semantic selector introduced for the created Case used subject `תקלה בחיבור לאינטרנט`, but TestCRM seed data already contains a Case with that exact subject.
- The resulting two matches correctly produced an ambiguous target; DAP did not guess and therefore did not present Step 12.
- The E2E-created Case now uses the unique stable business subject `תקלה בחיבור לאינטרנט - בדיקת DAP` throughout creation, post-close restoration, assertions and Steps 12/50 semantic selectors.
- This preserves the intended resolver invariant: Guide targets must resolve uniquely without relying on row position or injecting the runtime-generated Case ID into a Guide that was loaded before creation.
- Historical note: this exact DOM-identity invariant later passed the full Visual workflow; the rerun is no longer pending.


## Test-fidelity correction
- The temporary change that renamed the E2E-created Case subject to manufacture selector uniqueness was reverted. The original business scenario remains `תקלה בחיבור לאינטרנט`.
- Permanent rule: never change TestCRM/business data or workflow merely to make DAP Guide/E2E targeting pass. Ambiguity must be solved by DAP production targeting/anchors/context, not by tailoring the target application to the test.
- Steps 12/50 therefore require a proper target-identity solution before the full compatibility run can pass; the subject-only selector is intentionally not treated as an acceptable final solution because the real target application contains duplicate subjects.


## Runtime identity fix for created Case
- Removed the DAP-added `data-business-subject` attribute from TestCRM. Commit `32d4bd3`; target application is no longer tailored for this Guide.
- Added transient Web Guide runtime URL-fragment capture/materialization. Syntax: `{{step:<step-id>:frame-url-fragment}}` in locator or anchor values.
- Capture-source Steps wait for their frame URL to transition away from the preceding Step URL before capture, closing the Save-click/navigation race without fixed sleeps or TestCRM-specific route knowledge.
- Step 11 captures the persisted created-Case route. Steps 12 and 50 target `button.grid-open[data-go='<captured route>']`.
- E2E learner actions independently use the Case ID observed from the real post-Save route. The existing exact DOM identity invariant therefore verifies that DAP's runtime-bound target and the learner action resolve to the same element.
- No SQLite schema change and no TestCRM business/DOM adaptation were introduced for runtime identity.
- Historical note: a full Visual rerun was pending at this point; later full Visual PASS results supersede this requirement.


## Persistent TestCRM Guide database
- The visual TestCRM E2E no longer creates a random `%TEMP%\DAP.TestCRM.E2E\<guid>.db`.
- It resolves DAP's real database through `SqliteDatabaseOptions.CreateDefault()`, seeds/updates Guide `testcrm-create-case` there, reloads the 53 Steps from that repository, and launches DAP.exe with the exact same database path.
- Default path on Windows: `C:\ProgramData\DAP\Data\DAP.db`. `DAP_DATABASE_PATH` still overrides it explicitly.
- The run prints `DAP persistent guide database: <path>` so the inspected file is unambiguous.
- Dedicated SQLite repository tests continue to use isolated temporary databases; this change applies to the visual system run.


## DB is now authoritative for TestCRM Guide
- Normal TestCRM E2E no longer calls `DapTestCrmGuideSeed.CreateSteps()` and no longer writes every Step before each run.
- It initializes only the SQLite schema if needed, loads `testcrm-create-case` directly from the persistent DAP database, and runs that persisted definition.
- If the Guide is missing, E2E fails with an explicit initialization/reset-required error rather than silently recreating it.
- `DapTestCrmGuideSeed` is retained as the known baseline for a future explicit initialize/reset command; it is not the normal runtime Source of Truth.
- This protects future Instructor/Editor changes from being overwritten by E2E.


## Step 21 -> 22 fast-run race fixed
- Fast E2E exposed a harness race after `testcrm-save-closed-case`: `SaveSuccess()` was immediately followed by the Scenario 4 technical `location.reload()`, before E2E had observed Step 22.
- The clicked validation already reports completion to DAP.exe; the harness was destroying/reloading the document while DAP was reconciling the transition.
- E2E now waits for Step 22 immediately after the Step 21 Save action, then performs the technical Content reload, and later verifies Step 22 again on the replacement document.
- No TestCRM or production Runtime behavior was changed for this fix.


## Step 21 click/reload race root cause
- Fast run isolated the Step 21 -> 22 failure before the technical E2E reload.
- TestCRM's existing Case submit performs its server PUT and then a real `serverRefresh(location.hash)` / `location.reload()`.
- Production bubble click validation previously called `__dapReportValidation(stepId)` fire-and-forget. The document could be torn down before the Playwright binding invocation reached DAP.exe.
- WebBubblePresenter now gates cancelable native anchor/form-submit default actions until the DAP binding acknowledges the click completion, then replays the default navigation/submission. No TestCRM change or timing sleep was introduced.


## Full Fast E2E PASS after reused-target validation lifecycle fix — 2026-10-01
- Latest local Fast run completed the entire persisted 53-Step Guide successfully.
- Final output: `PASS: representative Customer -> Site -> Case -> Lead workflow, including dynamic Lead deletion and Case deletion, completed.`
- Root cause of the intermittent-looking Step transition failures was stale validation handlers on reused live DOM targets, not a need for longer Fast-mode waits.
- Step 16 reused the Case status control from Step 13 and was initially reported as Step 13. Step 21 reused the Save button from Step 18 and was initially reported as Step 18.
- Production `WebBubblePresenter` now rebinds both value and click validation handlers to the active Step when a target element is reused.
- Fast mode remains intentionally strict; do not mask future lifecycle/race defects by increasing waits without evidence.
- The 53-Step Fast run is now the current validated regression baseline.

## Cross-browser Web Runtime validation — 2026-10-01

- The representative 53-Step DAP.TestCRM Learner Web Runtime E2E now passes end-to-end on Playwright Chromium, installed Google Chrome, and installed Microsoft Edge using the same production Runtime and persistent Guide.
- The E2E runner supports `DAP_E2E_BROWSER=chromium|chrome|edge`; Chromium remains the default when the variable is unset.
- The final cross-browser regression sequence produced the same terminal PASS on Chrome, Edge, and Chromium: the complete Customer -> Site -> Case -> Lead workflow, including dynamic Lead deletion and Case deletion, completed successfully.
- The E2E default Playwright timeout remains 5 seconds. Cross-browser stability was achieved without increasing that timeout.
- Browser-dependent readiness was stabilized by relying on the TestCRM application-ready marker/current live Content frame rather than registering a redundant `DOMContentLoaded` wait after readiness had already been established.
- Click-validation advancement was hardened for navigation/frame-replacement races. The browser-side click handler removes the active bubble synchronously before reporting completion, so the Learner Runtime does not perform a redundant cross-frame bubble cleanup for that completed click. The reconciliation loop also rechecks DAP-owned click completion after Playwright reconciliation work and before delaying/reconciling again, allowing the Step to advance when completion arrives while the clicked document/frame is being replaced.
- This behavior is generic Runtime behavior; no Google-, Chrome-, Edge-, Chromium-, or TestCRM-specific progression workaround was introduced.

## Visual browser validation — 2026-10-01

- The full representative 53-Step DAP.TestCRM E2E also passes in `DAP_E2E_MODE=visual` on installed Google Chrome.
- The same full visual-mode E2E passes on installed Microsoft Edge.
- Visual mode preserves the demonstration-oriented cursor/typing behavior and TestCRM artificial server-processing delay, while the same production Learner Web Runtime, persisted Guide, readiness rules, target re-resolution, validation, and bubble lifecycle remain active.
- Together with the fast-mode regression runs, Chrome and Edge are now validated in both fast and visual execution modes; Chromium remains validated in fast mode on the current code baseline.

## Learner Web Runtime baseline complete — 2026-10-01

- The current Learner Web Runtime baseline is considered complete for this phase. The validated baseline is the persisted 53-Step TestCRM Guide, covering representative PeopleSoft/CRM behavior, with fast-mode PASS on Chromium, Chrome, and Edge and visual-mode PASS on Chrome and Edge.
- Remaining Web topics are intentionally separated from this baseline: future Instructor/Editor Target Capture semantics, author intent for repeated/Grid elements, and learner-facing handling of a Step whose target never becomes available.
- No permanent Learner dashboard is currently planned. The intended learner flow is external organizational launch/shortcut -> specific Guide -> Learner Runtime -> in-application bubbles -> completion. Organizations may own Guide/icon distribution and authorization through their existing mechanisms.
- No generic between-Step loading/progress GUI is currently planned. After a learner action, the bubble may disappear while the Runtime silently re-resolves the next target; the next bubble appears when its target is available. This avoids competing with the target application's own loading/status UI.
- A missing target is not, by itself, proof of Guide failure. Enterprise applications may legitimately take a variable time to produce the next screen or element. The Runtime must not skip to a different target or infer a substitute merely because the intended target is currently NotFound.

## Manual Learner Run — unified E2E modes

- Full Web human-learner walkthrough: `dotnet run --project tests\DAP.TestCRM.Web.E2E\DAP.TestCRM.Web.E2E.csproj -- --manual`.
- Full Windows human-learner walkthrough: `dotnet run --project tests\DAP.TestCRM.Windows.E2E\DAP.TestCRM.Windows.E2E.csproj -- --manual`.
- Manual mode uses the production Learner Runtime and persisted Guide; the E2E harness only owns environment/process orchestration. Once Step 1 is ready, no synthetic learner action is performed.
- Web completion remains owned by the in-browser completion bubble with explicit `סיום`; Windows manual mode uses the same guided DAP launch path and completion behavior.
- The same persistent Guide ownership rule applies: **Seed initializes. DB owns. Runtime consumes.**

### Focused manual learner runs
- `--manual-from-step <N>` remains the focused state-preserving handoff mode. Automation executes the real preceding workflow, waits until Step N is visibly ready, then stops synthetic actions and hands control to the human learner.
- This is preferred to launching DAP directly at Step N on fresh state because runtime captures and business context from preceding Steps are preserved.


## Web Bubble iframe promotion, completion UX, drag handle, and generic manual handoff — 2026-10-02
- Step 53 exposed a real presentation constraint: the target `#portal-header` lives inside the 52px-high `dap-header` iframe, so a correctly resolved child-frame bubble could be DOM-visible while its geometry was entirely clipped outside the iframe viewport.
- The production Web bubble presenter now keeps target resolution/validation in the owning child frame but promotes a clipped Overlay presentation to a top-level `#dap-guide-bubble-proxy`. This is a generic constrained-frame mechanism, not a TestCRM/Step-53 special case.
- Click-validation reconciliation was hardened against retiring-document/frame races. Validation completion is now awaitable and raced against in-flight bubble reconciliation so a completed click can advance even when Playwright work against the old document has not returned yet.
- The E2E Guide wait recognizes both the normal `#dap-guide-bubble` and promoted `#dap-guide-bubble-proxy` surfaces.
- Guide completion is now a production top-level `#dap-guide-completed` bubble with an explicit `סיום` action. It remains visible until the learner actually clicks `סיום`; DAP does not auto-click it and the intended product behavior is to finish the Guide/runtime without closing the learner's business browser.
- Regular bubbles, promoted iframe bubbles, and the completion bubble now expose an explicit `⠿` drag handle. Drag initiation is restricted to the visible handle hit area rather than the whole top row/bubble.
- Cursor behavior was corrected and manually verified: outside the handle the bubble uses the normal cursor from first presentation; over the handle it uses `grab`; during an active drag it uses `grabbing`; release returns the handle to `grab`.
- `--manual-from-step` in the representative E2E harness is now generic. It accepts any Step order present in the persisted Guide, automates the real preceding workflow, then pauses when the requested production bubble is ready. The old hard-coded Step-53 restriction and Step-53-only handoff diagnostics were removed.
- Manual verification after the cursor stabilization confirmed the drag interaction works as intended.
- Relevant commits in this sequence: `81d2ae2`, `bb49a06`, `e2abeac`, `2d766c0`, `d6b0383`, `70af73f`, `afe49cd`, `b753033`, `4523342`, `2c688dc`, `5099256`, `ea9dbeb`, `d0ba364`, `29a07af`, `ab82b10`.
- Historical note: at this point the normal 53-Step run had not yet been repeated after the drag-handle sequence. Later canonical 53/53 PASS baselines supersede this pending state.

## GitHub repository access

- Canonical repository: `shabydevelop-code/DAP-Platform`.
- Connected GitHub account `shabydevelop-code` has verified repository permissions: `admin=true`, `maintain=true`, `pull=true`, `push=true`, `triage=true`.
- In new chats, do not assume repository access is read-only. When write capability matters, verify permissions from repository metadata before concluding that write access is unavailable.
- DAP development may read and write this repository through the connected GitHub tools unless verified repository permissions change.


## TestCRM refactor verification and database separation (2026-10-02)
- The TestCRM refactor to `Server/`, `Web/`, and `data/` was pulled and verified locally.
- The TestCRM Web host starts successfully on `http://localhost:5200` after the WebRoot fix; the shared backend/API is on `http://localhost:5201`.
- The representative `Customer -> Site -> Case -> Lead` E2E workflow passes after the refactor, including dynamic Lead deletion and Case deletion.
- `tests/DAP.Data.Sqlite.Tests` contains infrastructure tests for DAP's SQLite persistence layer; it is not a TestCRM application/test-data directory and should remain under `tests/`.
- DAP product persistence and TestCRM business persistence are intentionally separate:
  - DAP product data: default `%ProgramData%\\DAP\\Data\\DAP.db` (or `DAP_DATABASE_PATH`).
  - TestCRM business data: `test-apps/DAP.TestCRM/data/testcrm.db`.
- Do not merge these databases: TestCRM is an external target/demo application, while `DAP.db` stores DAP guides, steps, targets, bubble/validation configuration, and related product state.


## Windows TestCRM client started (2026-10-02)
- Added a real WPF client at `test-apps/DAP.TestCRM/Windows/DAP.TestCRM.Windows.csproj`.
- Windows is an API client of the same TestCRM server used by Web; it does not access SQLite directly.
- This preserves one business-data owner: `Server -> data/testcrm.db`.
- Initial Windows functionality: load Customers, load Sites for the selected Customer, load Cases and Leads for the selected Site, and edit/save existing Sites, Cases, and Leads through the shared API.
- Important Windows controls have explicit UIA AutomationIds in preparation for DAP Windows Runtime targeting.
- Windows build/runtime verification is complete for the current canonical baseline; Windows Guided and Unguided both have verified 53/53 execution.

## Web E2E unified modes and Windows parity review — 2026-10-02
- The representative Web E2E project is now `tests/DAP.TestCRM.Web.E2E/DAP.TestCRM.Web.E2E.csproj`.
- Web E2E execution uses one canonical Customer -> Site -> Case -> Lead business flow. Mode switches must not create a second independently maintained business scenario.
- Unguided runs the canonical CRM flow without launching DAP.exe or synchronizing learner bubbles, while remaining sequenced by the persisted Guide. This baseline is locally verified PASS.
- The normal Fast + DAP path was locally re-verified after the unified-mode refactor: the persisted Guide loaded 53 Steps and the complete representative workflow ended in PASS.
- Historical Web From-Step verification used Guided Fast before N. That implementation has now been superseded by ADR-044 and must not be treated as the current contract.
- Current focused semantics are Unguided `1..N-1`, then Guided Manual or Guided Visual at N with resume context. `--manual-from-step` and `--visual-from-step` remain mutually exclusive.
- Visual frame-replacement waiting no longer requires observing the transient `#content-frame-next` Attached state. The harness waits for the stable replacement outcome/current ready Content frame, avoiding a race where the transient frame can be created/promoted before Playwright observes it.
- In full Visual mode the final Step 54 bubble is intentionally left visible briefly before the automated final action so the final Guide instruction can be observed.
- Current Web execution behaviors are therefore: full Fast, full Visual, full Manual via `--manual`, Unguided -> Manual at N, Unguided -> Visual at N, Unguided Full, and explicit Guide reset. Browser selection remains `chromium|chrome|edge` where applicable.
- Windows TestCRM was reviewed against the persisted 53-Step Web Guide. Most of the business workflow is relevant to Windows because both clients use the same server/API/business model, but Web-specific mechanics (DOM/iframe/frame URL/CSS targeting) must not be copied literally into Windows UIA tests.
- Known Windows parity gaps before building the Windows Unguided E2E: Case Resolution Notes is displayed/enabled but is not currently persisted in `CaseInput`; there is no Windows equivalent of the Web Step-15 activity-more interaction; and the Web Step-6 explicit Cases sort behavior does not currently have an equivalent explicit Windows implementation.
- Windows validation/delete confirmations currently use WPF `MessageBox`, which is a valid platform-specific equivalent rather than the Web PS alert/confirm DOM. Dynamic Lead status behavior and conditional Selected Service UI are present and are suitable for equivalent Windows business-flow coverage.
- That historical Windows parity plan is complete: the parity gaps were closed, the Windows Unguided UI Automation E2E was implemented and stabilized, and the production Windows Runtime/target resolution/bubbles were integrated and verified through the canonical 53-Step Guided baseline.



## TestCRM client/server separation — 2026-10-02

TestCRM is development/test infrastructure only and is never part of a customer DAP production package.

The test application now has explicit deployment boundaries:
- `Server/DAP.TestCRM.Server.csproj` — shared API, business rules, validation, FieldChange behavior and `data/testcrm.db`. It contains no Web static UI.
- `Web/DAP.TestCRM.Web.csproj` — Web host and `wwwroot` static UI only. It proxies `/api` to the shared backend.
- `Windows/DAP.TestCRM.Windows.csproj` — WPF client. It calls the shared backend directly and has no dependency on the Web client.

Development ports:
- shared backend: `http://localhost:5201`
- Web host: `http://localhost:5200`

Architectural invariant: Web and Windows may depend on the shared backend contract, but neither client may depend on the other client. A Windows-only test deployment must work with Server + Windows after the Web directory is absent; a Web-only test deployment must work with Server + Web after the Windows directory is absent.

The old combined root `DAP.TestCRM.csproj` and root launch profile were removed so the Web static files cannot accidentally become a backend dependency.


## Windows Unguided canonical E2E baseline — 2026-10-02
- The Windows Unguided UI Automation E2E is implemented in `tests/DAP.TestCRM.Windows.E2E` and executes the shared canonical core scenario from `tests/DAP.TestCRM.E2E.Common` against the real WPF TestCRM client.
- Local verification reached terminal PASS: `PASS: Windows Unguided canonical Customer -> Site -> Case -> Lead core scenario completed.`
- The verified core covers Customer -> Site -> Case -> Lead navigation, Case creation/save/status FieldChange behavior, Resolution Notes enablement, required-field validation handling, Lead creation/save/status transitions, conditional Selected Service handling, Lead deletion confirmation, and return to Portal.
- WPF ComboBoxes used by the scenario expose a custom UIA ValuePattern through `AutomationComboBox`, allowing deterministic value selection without physical popup interaction.
- Case/Lead FieldChange responses are applied locally in the WPF client instead of immediately reloading the persisted record, preventing transient status changes from being reverted before Save.
- Windows E2E navigation now synchronizes on the rendered destination screen rather than assuming an async click has completed. Site breadcrumb targeting uses stable UIA identity (`AutomationId=Breadcrumb` plus the site name), and Lead creation retries safely until the form is ready.
- Modal WPF MessageBoxes are handled as real platform dialogs. Expected validation/delete dialogs are dismissed/confirmed explicitly; unexpected informational OK dialogs block further E2E actions until dismissed so the scenario cannot continue behind a modal window.
- Default Windows E2E wait timeout is 5 seconds.
- The activity-more interaction remains a Windows-specific UI area to align with the Web behavior; the canonical scenario no longer incorrectly calls validation dismissal immediately after `ShowMoreActivity`.
- Historical note: this was the earlier Windows Unguided core milestone before full 53-Step parity. The canonical Windows Guided and Unguided paths are now complete at 53/53.

- Windows/Web activity-more parity correction: Web renders `#activity-more` but defines no click handler or alert for it. The Windows-only `MessageBox` ("אין פעילויות נוספות להצגה.") was therefore removed; `ActivityMoreButton` now has the same no-alert/no-op business behavior as Web. This correction is included in the later verified 53-Step Windows baseline.
- Historical note: this Windows parity milestone has been completed. Both Windows Guided and Windows Unguided now cover the canonical 53-Step business workflow.

- **Windows canonical 53-step Unguided milestone: PASS (locally verified 2026-10-02).** `CanonicalCrmScenario.Run53Async` completed the full Customer -> Site -> Case -> Lead flow and terminated with `PASS: Windows Unguided canonical 53-step Customer -> Site -> Case -> Lead scenario completed.` The Windows E2E now synchronizes async sort/create/delete/navigation transitions in the test harness. Test-only row AutomationIds were removed from the Windows target application; E2E target resolution remains the responsibility of the test harness. No full-53 PASS is claimed for DAP Runtime/bubbles yet; this milestone is the Unguided Windows business-flow baseline.


## Numeric Guide/Step database identities — 2026-10-02
- DAP SQLite persistence now uses numeric internal primary/foreign keys for `Guides.Id`, `GuideSteps.Id`, `GuideSteps.GuideId`, and `TargetAnchors.GuideStepId`.
- Human-readable stable identifiers are stored separately as `Guides.Key` and `GuideSteps.Key`.
- Existing databases using the legacy TEXT primary-key schema are migrated automatically by `SqliteDatabaseInitializer`; Guide keys, Step keys, ordering, bubble/validation data, frame/context data, and TargetAnchors are preserved.
- Repository APIs continue to accept the stable textual Guide key, so runtime launch semantics do not expose database row IDs.
- The canonical TestCRM Web Guide key is now `testcrm-web-canonical-workflow`; the E2E harness migrates the previous `testcrm-create-case` key in place and assigns the display name `TestCRM Web Canonical Workflow`.
- `DAP.Data.Sqlite.Tests` now covers both a fresh numeric-ID round trip and migration from the legacy TEXT-ID schema.


## Self-contained TestCRM Web E2E startup — 2026-10-02
- The normal TestCRM Web E2E runner now starts both required TestCRM processes itself in every execution mode, not only in `--manual-from-step`.
- The runner owns `DAP.TestCRM.Server` on `http://localhost:5201` and `DAP.TestCRM.Web` on `http://localhost:5200`, waits for the Web host to become ready, and then runs the browser/DAP scenario.
- E2E cleanup now terminates and disposes both the owned Web host and backend process trees in addition to the owned DAP process.
- A normal full Web E2E run therefore no longer requires manually starting TestCRM servers in separate terminals.


## Verified post-migration Web baseline — 2026-10-02
- Local `DAP.Data.Sqlite.Tests` verification: `DAP SQLite guide persistence and legacy ID migration: PASS`.
- The real DAP database at `C:\ProgramData\DAP\Data\DAP.db` was migrated successfully and the canonical Web Guide was reset as `testcrm-web-canonical-workflow` with 53 Steps.
- The full canonical Web E2E passed after the persistence migration: `PASS: representative Customer -> Site -> Case -> Lead workflow, including dynamic Lead deletion and Case deletion, completed.`
- The Web E2E runner was then changed to start and clean up TestCRM Server + Web itself.
- A second full canonical Web E2E run passed with this self-contained topology, confirming that no separately pre-started TestCRM server terminals are required.
- Current stable Web baseline: numeric persistence IDs + stable textual keys + 53-Step persisted Guide + self-contained full E2E PASS.
- Historical note: Windows Learner Runtime was the next milestone here; it has since progressed to the full verified 53-Step Guided path.

## Windows persisted Guide 12-Step milestone — 2026-10-03

- **Windows Guided persisted Steps 1 -> 12 are locally verified PASS.** Terminal result: `PASS: DAP Windows Learner Runtime persisted Steps 1 -> 12 with real UIA targets, runtime capture, and bubbles.`
- Step 11 captures the newly created Case identifier from the live Windows UI and persists it as runtime Step capture for reuse by Step 12.
- Step 12 resolves the created Case row from `CasesGrid` using the captured identifier and contextual anchors. For small already-realized grids, Windows target resolution now uses the same ordinary `DataItem` row filtering principle already used successfully for `SitesGrid`: enumerate scoped rows, apply the descendant identity, require a unique result, and do not guess.
- The TestCRM Case fixture is bounded on server startup so the primary Site keeps only the newest 10 pre-existing Cases; the guided scenario therefore exercises a small deterministic grid instead of historical database pollution from prior runs. This is test-fixture hygiene, not a production Runtime shortcut.
- Windows learner bubbles now support a visible drag handle and a directional pointer toward the resolved UIA target. Initial placement tries non-overlapping sides of the target and changes side when the preferred placement would not fit. Dragging updates the pointer direction.
- The Windows E2E driver selects the created Case row through `SelectionItemPattern` before the physical double-click fallback, matching the existing safe WPF DataGrid interaction pattern.
- The 5-second E2E timeout policy remains unchanged. No timeout increase was used to obtain the PASS.
- Closed-target rule remains unchanged: TestCRM source may be inspected for diagnosis and learning, but production target resolution relies only on runtime-observable UIA data.

## Windows bubble drag persistence — 2026-10-03

- Windows learner bubbles now preserve a manual drag position for the lifetime of the active Step.
- After the learner drags a Windows bubble, the Runtime no longer reapplies automatic placement on subsequent reconciliation cycles for that same Step.
- The directional pointer is hidden after manual dragging, matching the established Web learner-bubble behavior.
- When the active Step changes, manual-position state is reset; the next Step returns to automatic placement and shows its pointer again.
- The interaction remains presentation-only: dragging does not change target identity, validation, runtime capture, or Step progression.

## Guide progression hardening — persisted postconditions

Work toward additional Windows Steps is paused while learner-flow ownership is hardened across Web and Windows.

Implemented:
- shared persisted Step completion conditions in Core and SQLite;
- Web Runtime enforcement of persisted post-action conditions;
- Windows Runtime enforcement of persisted post-action conditions;
- Windows persisted context guards;
- explicit persisted Web runtime capture using StepCaptureDefinition;
- TestCRM Web navigation transitions now persist destination/post-action checks for the canonical flow;
- Windows canonical Steps 1–30 now persist key destination/state checks rather than relying only on E2E waits;
- the obsolete Windows unguided final assertion that always expected DeleteCaseButton was removed.

The design rule remains: Guide/DB owns what completes a Step; Runtime owns how it is observed; E2E acts only as a synthetic learner.


## Windows manual learner handoff — 2026-10-03

The Windows TestCRM E2E runner supports `--manual-from-step <N>`, matching the Web handoff model. The runner starts backend and Windows TestCRM, executes Steps `1..N-1` without DAP to establish the real business state, preserves required runtime captures in resume context, then launches the production Windows Learner Runtime directly at Step N. Launching at N is valid only because the real preceding workflow and its capture context were preserved; starting at N on a fresh application state remains invalid.

`--manual-from-step` cannot be combined with `--unguided`. When the operator finishes the manual session and presses ENTER in the E2E console, the runner cleans up the DAP, TestCRM, and backend processes it owns.

### Windows E2E mode parity with Web — 2026-10-03
- Windows canonical E2E now accepts the same `DAP_E2E_MODE=fast|visual` vocabulary as Web; `fast` remains the default and any other value fails explicitly.
- `--guided` + `fast` follows the canonical persisted Windows Guide; after the new seed is reset into `DAP.db` this sequence contains 54 Steps.
- `--guided` + `visual` runs the same persisted canonical Guide and the same UIA action driver, but adds observable cursor movement and visual pacing before learner actions. No alternate test scenario or production shortcut was introduced.
- Windows also accepts `--visual-from-step <N>`, matching Web: prior Steps execute as Unguided bootstrap, then DAP starts at Step N and automation continues in Guided Visual mode.
- Existing `--manual-from-step <N>`, `--manual`, and `--unguided` remain available. Unguided has no Fast/Visual mode and ignores `DAP_E2E_MODE`.
- The 5-second technical timeout policy is unchanged. Visual delays are presentation pacing only and do not increase resolver/synchronization timeouts.
- Focused Windows Visual From Step is now locally verified at Step 47. Full Windows Guided Visual Full remains a distinct run and should only be marked PASS when explicitly executed.

- Runner precedence hardening: both Web and Windows now read `DAP_E2E_MODE` only for full `--guided`. `--unguided`, `--manual`, `--manual-from-step <N>`, and `--visual-from-step <N>` ignore stale shell mode values, so each public command has deterministic semantics independent of the previously executed command.

### Unguided From-Step bootstrap with resume context — 2026-10-04

Implemented symmetrically for Web and Windows:
- `--manual-from-step <N>`: Steps before N run without DAP/bubbles; DAP starts at N and control is handed to the human learner.
- `--visual-from-step <N>`: Steps before N run without DAP/bubbles; DAP starts at N and automation continues in Visual mode.
- Bootstrap preserves real business state and carries earlier Guide runtime captures into DAP through a validated resume context.
- The current canonical TestCRM capture used later in the flow (created Case identity) is preserved across the bootstrap/Guided boundary on both platforms.
- Full `--guided`, full `--manual`, and full `--unguided` behavior is unchanged.
- No timeout was raised.

Focused From-Step execution is now locally verified on both Web and Windows at Step 47, including Unguided bootstrap, resume context, DAP start at Step 47, and Guided Visual completion.

- **Web From-Step bootstrap fix — 2026-10-04:** the first Unguided-bootstrap run exposed a harness invariant leak: `MoveTo()` still required the active production DAP bubble target during Steps before N, even though DAP is intentionally not running there. The invariant is now disabled only during the bootstrap prefix and is re-enabled immediately when DAP starts at Step N, before the first Guided learner action. Commit: `3957f99195774bce2b3d0b7710d98484442174d6`. The subsequent Web `--visual-from-step 47` rerun completed successfully.

- **Web/Windows Visual cursor parity — 2026-10-04:** Web Visual cursor travel now matches Windows Visual timing and easing: 12 frames, 18 ms per frame, cubic ease-out, and a 120 ms target dwell. This changes only visible cursor travel; Fast mode and technical timeouts are unchanged. Commit: `dcc8f7c52b26dff1eb4e9eb0c6a218e7e45accc6`.

- **Web Visual From-Step re-verified after cursor parity — 2026-10-04:** `--visual-from-step 47` passed locally after pulling the Web cursor pacing alignment. The run completed successfully with Steps 1–46 as Unguided bootstrap, DAP starting at Step 47 with resume context, and Guided Visual execution from Step 47 onward. The Web cursor pacing now matches the Windows Visual movement profile and was manually observed as satisfactory.

### Windows completion bubble — 2026-10-04

The Windows learner completion path no longer uses the foreground/topmost operating-system message box. Completion is now part of the Guide runtime lifecycle, matching Web: after the final Step, a DAP-native completion bubble appears with localized `Learner.GuideCompleted` text, localized `Learner.Finish` button, and the standard drag handle. The target application remains open after dismissal.

Automated Guided runs on both Web and Windows now exercise this same production completion UI and activate its real Finish action before expecting DAP.exe to exit. Manual runs leave that action to the learner. The former `--show-completion` launch switch was removed because completion is no longer optional.

Implementation commits: `8b4e9b6834c58777fed65e5de5cdaac141507ee2`, `0e722fc845be689afb97898412f82267d483cb7d`, `9c6d1343eaaa14e9683588c7a05e957086ebf5c3`, `2de50a27456ad9149ffe24662436d34a5f4e4e32`, `e99b0ccf47cd0e8763b5a2bc3a522e5439330968`, `04abdc8ae73afe9734f5e98662b01ba5d990823e`, `d715318ffbdd41e075c441111f9138b2b61cd295`, and `640f4165f48754d52e8d53bb5d261c2d9f7c3b0d`. Local regression execution is still required after pull.

- **Web Manual From-Step lifecycle fix — 2026-10-04:** the focused manual Web handoff no longer blocks on `Console.ReadLine()`. After handoff, the runner waits for either DAP.exe to finish the Guide or the owned browser/page to close; either condition returns through normal E2E cleanup. Ctrl+C remains the explicit early-stop path. Commit: `26dc115ceff41cfcfd23b24dc7b213a02158061a`.

### Centered information bubble capability — 2026-10-04

Implemented cross-platform support for targetless informational Guide Steps. A persisted Step can now use `Target = null`, `BubblePlacement.Center`, and `StepAdvanceMode.Manual`; Web and Windows present it in the center of the screen with the ordinary DAP bubble styling, no pointer/highlight, Step progress, and a localized confirmation button. Runtime validation rejects centered Steps that incorrectly define a target, automatic validation, capture, or completion conditions.

Web and Windows Guide completion now use the same general centered-bubble presentation primitive. This also corrects Web completion positioning from horizontal-only centering at the top of the viewport to true horizontal and vertical centering.

SQLite needs no schema migration because `BubblePlacement.Center` is stored through the existing textual `BubblePlacement` field. A persistence regression case was added for a centered targetless Step.

Implementation commits include `bda50be89cd15ea067aff90bc3010c5a5855dd94`, `c2810f3867d7fc67924c20aa1a4d24e92348346f`, `32561e93823ed2bc2b66c2ef39cf4f6fba364116`, `a74fefaf82b70ba359b0263fe77b239e0f7899e4`, `6b59895caf01e1259bc43ea11a11b806232de266`, `cd57a1f8331c0f9c130b26ef0cd3d4579284c1f2`, `8ef79db91741848dd2efca18a8c9c143247f22c2`, `a527510dcc36ce4984bc043f7a938de2914220b1`, `33254249f248be5264bfea8f3260686be630e8f7`, `7ed0cd2acc8722dcbd63e9de436d0c693ef9624e`, and `9a0c45ebb4229dc18c2ae2a51bee287a478ed6d4`.

Local Web/Windows execution is still required after pull before marking the new centered presentation path verified.

### Canonical centered information Step — 2026-10-04

The Web and Windows canonical repository seeds now include a real centered information Step at order 51, immediately before deleting the Case created during the learner flow. Text: `שים לב: בשלב הבא נמחק את הפנייה שיצרת במהלך הלומדה.` The Step has no target, uses `BubblePlacement.Center`, uses Manual advance, and blocks Guided progression until the learner presses the localized `אישור` action. Subsequent deletion/confirmation/header Steps move to orders 52/53/54. Unguided execution preserves the persisted order but treats this pure information Step as a no-op because no DAP presentation surface exists. Existing persisted Guides are not silently overwritten; run `--reset-guide` before testing the new 54-Step seed.

- Web focused From-Step bootstrap treats the new Step 51 information pause as presentation-only setup when the requested start Step is later than 51. In that case DAP is still off and the E2E does not attempt to press `אישור`; when Step 51 is within the Guided portion, the real centered bubble is shown and its confirmation action is exercised. Commit: `b9a4ad3b4af8b6c415c8e05d4c52e96c94f4395e`.

### Guide reset ordering fix — 2026-10-04

`--reset-guide` previously saved Steps one-by-one with upsert semantics. Inserting a new Step into the middle of an existing Guide could therefore collide with the existing `UNIQUE(GuideId, StepOrder)` constraint before later Steps had moved to their new orders. This surfaced when the new centered information Step was inserted at order 51.

`SqliteGuideStepRepository.ReplaceStepsAsync` now replaces the full Step set inside one SQLite transaction: it resolves/creates the Guide row, deletes the old Step set, inserts the complete replacement sequence and all anchors/captures/completion conditions, then commits. Web and Windows `--reset-guide` both use this replacement operation. A persistence regression test now covers replacing a two-Step Guide with a three-Step Guide that inserts a centered information Step in the middle and shifts a later Step order.

Implementation commits: `e89a4af954f502b44cedef8949b4578cee3524b7`, `496a9f47985ebe12bfb328bff6c2b7c0952b51f9`, `1ae8c109a9e42c6ca1680cae4d7e8476ad99c264`, `b5227c78d2a0a3866e6cf8f9e2e95aee3d83ccb1`.

### Visual cursor on DAP-owned bubble actions — 2026-10-04

Visual mode now treats DAP-owned learner actions as visible learner interactions rather than invoking them invisibly. On both Web and Windows, the real operating-system cursor moves with the same Visual motion contract to the centered information `אישור` action before confirmation and to the completion `סיום` action before finishing the Guide. Fast mode keeps the direct/non-visual action path.

Web reuses the existing `MoveTo` animation with an explicit opt-out from the active target invariant for DAP-owned overlay controls, because centered information and completion bubbles intentionally have no application Target. Windows exposes the existing `VisualTarget` cursor animation so the E2E helper can apply it to DAP UIA buttons as well as CRM controls.

Implementation commits: `47bf7ab38e9ce1df00180ab8ec9f5acef214ba82`, `3e7accf6e62bb6dfe7843f4ae5b279c25a2221d1`, `bbfa47f47aca5d6afc9bdc0c0c192e66d406590b`, `ad36a7865d75b6ed86621a58f943e0610e29e19b`.

### Web Visual real OS cursor — 2026-10-04

The Web E2E Visual runner no longer injects `#dap-e2e-cursor` or `window.__dapE2ECursor`. Visual target motion now converts the Playwright target bounding box from browser viewport coordinates to Windows screen coordinates and moves the real operating-system cursor through Win32 `GetCursorPos` / `SetCursorPos`.

The existing Visual motion contract is preserved: 12 cubic-ease-out frames, 18 ms per frame, and 120 ms dwell. Playwright still performs the actual browser interaction after the physical cursor reaches the target, preserving DOM-level synchronization and the active-Guide-target invariant. DAP-owned centered information/Finish controls use the same physical cursor path with the target invariant explicitly disabled because those controls intentionally have no application Target.

Implementation commits: `0cac505d40eb092c52a5b5eeed32d2d7811814cd`, `9fc6768a338108f8caceceeed36e517bad53030b`.

Local verification is complete for the focused Visual path on both platforms: Web `--visual-from-step 47` passed with the real OS cursor after removal of the synthetic DOM cursor, and Windows `--visual-from-step 47` also passed with native cursor movement to CRM targets, `אישור`, and `סיום`.

### Focused 54-Step Visual verification — 2026-10-04

After resetting both canonical Guides to the current 54-Step seed, the focused Visual path from Step 47 was locally verified on both platforms.

- **Web — PASS:** `--visual-from-step 47` completed through Steps 47–54, including the centered targetless Step 51, real-cursor travel to `אישור`, final Case deletion, Step 54 navigation, real-cursor travel to `סיום`, and clean Guide completion. The old synthetic DOM cursor is no longer used.
- **Windows — PASS:** `--visual-from-step 47` completed through the same aligned Steps, including the centered Step 51 and native cursor travel to both DAP-owned actions `אישור` and `סיום`.
- These focused Visual PASS results verify the new centered-information and real-cursor behavior across Web and Windows. They do **not** yet replace the historical full four-path 53/53 matrix; full 54-Step Guided/Unguided regression runs remain pending.

### Windows learner bubble viewport behavior and production-package verification — 2026-10-04

Windows target-attached bubbles now distinguish Step semantics from presentation visibility. Initial Step entry may perform the existing one-time viewport adjustment, but later learner scrolling is not fought by the Runtime. Reconciliation continues to resolve and validate the active Step; bubble presentation independently hides the attached bubble when its target is clipped outside a vertically scrollable viewport and restores it when the target is visible again. This prevents a bubble from being pinned or visually dragged along a viewport edge while preserving validation/progression behavior.

A UIA race discovered during the production-package Visual run was also hardened: when a WPF target loses visible bounds between reconciliation and presentation, the learner Runtime treats that specific stale-target condition as transient, hides the stale bubble, and resolves again instead of terminating DAP.

Implementation commits: `b061fa3446a3f53c410714c1255ec684fff6c307`, `ce10e7b7f6a89f1fdecf064318359578a2513c5e`, `437c7f176bdee0ee9132cb699c792a433c93c42d`, and `9001e806b1e4ae4e50aeac515cec15a3b8c7e3b6`.

Verification against the framework-dependent published package at `C:\DAP-Production` is complete for Windows Guided Visual: the current 54-Step canonical workflow completed 54/54 and runner-owned DAP exited cleanly with no surviving DAP process. A focused `--manual-from-step 11` check then manually scrolled the active target out of view and back: the bubble did not remain pinned/dragged at the viewport edge, disappeared when the target left the visible scroll viewport, and returned beside the target when it became visible again. **PASS.**

### Customer production diagnostics package — 2026-10-04

The customer Production package now has an explicit diagnostics payload rather than requiring the source repository on the customer machine. `scripts/Publish-Customer-Package.ps1` publishes the framework-dependent DAP product plus prebuilt TestCRM Server, Web client, Windows client, and both E2E runners under `<Production>\Diagnostics`. Customer diagnostics therefore run without `dotnet run`, without project files, and without compiling DAP/TestCRM on the customer machine.

The packaged runners use `DAP_DIAGNOSTICS_ROOT` to resolve the prebuilt TestCRM binaries. Guided runs launch the exact sibling Production `DAP.exe`; Unguided runs intentionally omit DAP and provide the environment/application sanity baseline. Manual-From-Step and Visual-From-Step retain their canonical Unguided bootstrap before starting the same Production DAP at the requested Step.

`Diagnostics\Run-Diagnostics.ps1` exposes the six canonical modes on both Web and Windows: Fast, Visual, Manual, Unguided, ManualFromStep, and VisualFromStep. It refreshes only the dedicated canonical TestCRM Guide for the selected platform before each diagnostic run so the customer check uses the packaged 54-Step definition.

The intended customer sequence is: run Unguided first to prove the new environment and TestCRM automation path without DAP, then run Guided Fast/Visual and focused/manual modes to introduce DAP into the same known scenario.

## Windows full 54-Step Fast/Visual verification — 2026-10-05

Windows full Guided execution is now locally verified PASS in both E2E presentation modes against the persisted 54-Step canonical Guide.

- **Fast — PASS 54/54:** terminal result: `PASS: DAP Windows Learner Runtime completed all 54 persisted Guide Steps with real UIA targets, runtime capture, modal targeting, centered information, and bubbles.`
- **Visual — PASS 54/54:** the same full 54-Step Guided scenario completed with the same terminal PASS.
- The Step-8 `CaseSubject` regression was resolved by restoring focused-value synchronization in the Windows synthetic learner before TAB commit. After `SetValue`, the E2E driver now waits until the requested value is observable while the edit remains keyboard-focused, retains the UIA value-change synchronization, and only then commits through TAB. This restores the synchronization contract that had previously closed the same regression; no Runtime-specific workaround or timeout increase was introduced. Implementation commit: `889ee17d3e34692022085760dea2496b31c0cb69`.
- Fast and Visual use the same application-action path. Visual adds only cursor presentation and display pacing through `VisualTarget` / `VisualPause`; it does not own alternate CRM workflow logic, target scrolling, validation semantics, or business actions. Physical mouse actions required by the target application, such as WPF DataGrid double-click, remain the same real action in both modes; Fast may jump the cursor directly while Visual animates travel first.
- The stale packaged-diagnostics environment isolation fix was also verified in the clean repository run: repository E2E starts its own isolated temporary Server/Windows/DAP outputs rather than silently reusing `C:\DAP-Production\Diagnostics` merely because `DAP_DIAGNOSTICS_ROOT` remains set in the shell.

This verifies the complete Windows Guided 54-Step path in both Fast and Visual modes. It does not by itself complete the full cross-platform Guided/Unguided 54-Step matrix; remaining Web/Unguided verification must be tracked separately.

## Web full 54-Step Fast/Visual verification — 2026-10-05

Web full Guided execution is now locally verified PASS in both E2E presentation modes against the persisted 54-Step canonical Guide.

- **Fast — PASS 54/54:** terminal result: `PASS: representative Customer -> Site -> Case -> Lead workflow, including dynamic Lead deletion and Case deletion, completed.`
- **Visual — PASS 54/54:** the same complete canonical Guided scenario produced the same terminal PASS.
- Fast and Visual use the same application-action path. The Web E2E no longer changes per-character typing delay by mode; Visual differences are presentation-oriented cursor movement and pacing around the same learner actions. Implementation commit: `401f31a582797fcb9217e521e40c0557196736c1`.
- Repository Web E2E isolated output now mirrors the TestCRM Web `wwwroot` into the owned temporary run directory so the self-contained Web host serves the actual client instead of starting without static assets. Implementation commit: `430c90176f82e636fbcea6685cd15f387b0f041d`.
- Web E2E cleanup now covers startup/navigation failures as well as the scenario body. A reproduced navigation failure had left the runner-owned Web and Backend processes on ports 5200/5201; the outer cleanup scope now closes those owned processes on that failure path. Implementation commit: `d23c6d254b7d12d057d4ea713886db8089a8ca5b`.

Together with the already verified Windows Guided Fast and Visual 54/54 runs, the complete Guided 54-Step Fast/Visual baseline is now PASS on both Web and Windows. Unguided/full-manual matrix verification remains separate.


## Windows Hybrid learner verification — 2026-10-06

Windows Hybrid testing is now complete and locally verified PASS.

- The canonical Windows Guide was reset to the current persisted 54-Step definition before verification.
- Full manual Windows learner execution had already proved that the persisted Guide data is sufficient to drive the production learner without Playwright and without adding test-only detection behavior to the Runtime.
- The Windows Hybrid path was then exercised end-to-end and completed successfully: `PASS: hybrid Web Guide completed.`
- Hybrid execution preserves the original persisted Step numbering. Disabled Steps are skipped without renumbering the Guide.
- The verified disabled/redundant Steps are 24, 25, 30, 31, and 42–45. Step 29 remains active because it performs a meaningful business action.
- Skipping repetitive Steps must never remove meaningful saves, validations, deletes, navigation, or other business-state transitions required by later Steps.
- Persisted automation values may populate controls where explicitly configured by Guide data. Meaningful learner actions remain manual where the Guide requires them.
- Runtime owns Step completion and progression. The E2E/Hybrid orchestration must not manufacture completion by adding activity/detection logic that should instead be represented by persisted target, validation, capture, or completion-condition data.
- Manual/Hybrid learner interaction is not constrained by the 5-second E2E technical timeout while waiting for the human learner. The existing 5-second rule remains the technical timeout policy for automated waits; it was not increased to make Hybrid pass.
- Windows Hybrid synchronization was corrected so disabled Steps and Web/Windows handoff do not corrupt progression or business context.
- Relevant Hybrid stabilization commits include `ac6eaaa`, `fa08a52`, and `9bb497e`.
- The successful result confirms the intended architecture: persisted Guide data remains the source of truth, production Runtime performs deterministic resolution/validation/progression, and test automation does not become a second hidden learner engine.

This closes the Windows Hybrid verification milestone for the current canonical workflow.

## Windows Hybrid migration — 14-step implementation record — 2026-10-06

The Windows E2E/Hybrid transition was performed as an explicit 14-step cleanup/alignment sequence. This record is retained so the final Hybrid PASS does not hide which legacy automation responsibilities were removed.

1. `e3a0adc` — **Remove guide outcome detection from Windows select driver.** Select actions stopped deciding whether the Guide outcome had completed; that responsibility belongs to persisted Guide validation/completion and Runtime progression.
2. `c25ebc1` — **Remove save outcome detection from Windows E2E driver.** Save actions became learner actions only; the action driver no longer independently determines the resulting Guide/business outcome.
3. `d685124` — **Capture Windows created case after Runtime advances.** Created-case capture was moved behind the production Runtime transition so capture does not race or replace Runtime progression.
4. `c4fe463` — **Align Windows created case capture with Web runner.** Windows and Web now follow the same ownership model for capturing the created Case used by later scenario actions.
5. `782f842` — **Preserve Windows focused-bootstrap case capture.** Focused/bootstrap execution retains the Case identity needed after handoff without restoring the removed legacy outcome detector.
6. `edb732a` — **Remove delete outcome detection from Windows E2E driver.** Delete actions no longer contain a parallel test-side detector for successful deletion; production-observable Guide/Runtime conditions own completion.
7. `454bb55` — **Remove automatic dialog dismissal from Windows E2E driver.** The driver no longer silently closes application dialogs as a legacy convenience; dialog interaction must be an intentional learner/scenario action.
8. `91d4469` — **Align Windows navigation actions with Web runner.** Windows navigation actions were reduced/aligned to perform the intended learner action rather than embedding extra outcome synchronization.
9. `5290d2d` — **Remove created-case outcome assertion from Windows action driver.** The action layer stopped asserting the business result that should be observed after Runtime progression.
10. `23ccc6b` — **Align Windows text entry with Web learner actions.** Windows text entry now represents the learner edit/commit action without carrying legacy platform-specific progression behavior.
11. `53eee3b` — **Remove Windows select-value polling from action driver.** Selection actions no longer poll the selected value as a second completion mechanism; configured Guide/Runtime observation remains authoritative.
12. `8130e60` — **Separate Web select actions from outcome synchronization.** The corresponding Web action path was split so Hybrid uses the same cross-platform rule: perform the learner action separately from observing its outcome.
13. `39519e8` — **Use explicit Web E2E mode and browser arguments.** Hybrid Web execution receives its mode/browser configuration explicitly instead of inheriting stale environment-driven behavior.
14. `e6a133b` — **Validate explicit Web E2E Visual mode arguments.** Explicit Hybrid/Web argument handling was validated so Visual configuration is intentional and cannot silently drift through inherited state.

### Result of the 14-step migration

The sequence removes the old pattern in which Windows E2E action helpers both performed an action and independently tried to prove/force its outcome. The resulting boundary is: **action driver performs the learner action; persisted Guide + production Runtime observe validation/completion and advance the Guide; orchestration only synchronizes with the resulting production state.**

Case identity capture remains only where later synthetic scenario actions genuinely need an identifier; it occurs after Runtime progression and is not a substitute for Guide completion detection.

The same separation was applied to the Web side where required for Hybrid handoff, particularly select-action outcome synchronization and explicit mode/browser configuration.

After these changes, Windows Hybrid testing completed successfully. This 14-step sequence is therefore part of the verified Windows Hybrid baseline and must not be reintroduced through legacy action-driver polling, automatic outcome assertions, automatic dialog dismissal, or duplicate completion detection.


## Persisted Guide summary Step 55 — 2026-10-06

The canonical Web and Windows repository seeds now contain **55 persisted Steps**. Step 55 is the centered manual Guide summary, persisted in Guide data rather than synthesized by a separate completion-bubble path.

- Web Step 55: `testcrm-guide-summary`.
- Windows Step 55: `testcrm-windows-guide-summary`.
- Text: `המדריך הושלם בהצלחה`.
- Target: none.
- Placement: Center.
- Advance mode: Manual.
- Canonical E2E now waits for persisted Step 55 and activates the normal centered-bubble confirmation action.
- The former E2E-specific wait/click path for a special completion bubble is retired from the canonical runners.
- `--reset-guide` now restores all 55 Steps, so reset no longer drops the persisted summary Step.

The previously verified 54/54 Fast/Visual results remain historical verification of the pre-summary persisted Guide. They are **not** silently relabeled as 55/55. A fresh full 55-Step regression is required before claiming 55/55 PASS.

## Current Manual/Hybrid execution baseline — 2026-10-06

Current `main` now exposes the canonical learner E2E runners as Manual or Hybrid only for both Web and Windows, with `--reset-guide` retained as Guide maintenance and `--published-dap` retained as a packaging/path option.

Windows no longer exposes Guided, Unguided, Fast, Visual, Manual-From-Step, Visual-From-Step, or `DAP_E2E_MODE` in the current runner.

Hybrid metadata is now part of the persisted Guide model and SQLite storage:
- `IsEnabled` is persisted and loaded.
- `AutomationValue` is persisted and loaded.
- Existing databases are upgraded in place with the two GuideStep columns.
- Production Web and Windows runtimes skip disabled Steps while preserving persisted Step order/identity.
- Current disabled canonical Steps remain 24, 25, 30, 31, and 42–45.
- Windows Hybrid performs only persisted value-entry actions. Buttons, navigation, dialogs, and centered information confirmations remain learner actions.
- Runtime remains the owner of validation, completion, capture, and Step progression.
- Persisted Step 55 is the Guide summary and is the terminal Guide Step. The former additional Runtime completion bubble after Step 55 has been removed.

A fresh local build and Manual/Hybrid regression is still required before recording a new PASS for this baseline.

## Web extension restored to current main — 2026-10-06

By explicit user authorization, the production Web extension components were recovered from the `web-extension-runtime` branch into current `main`. The recovery was limited to the browser-extension architecture and its required adapter/native-host components; unrelated runner/history code was not restored.

Current production Web composition now routes through `ExtensionWebBrowserAdapter` and `AdapterWebGuideRuntime`. The production `DAP.Runtime.Web` project no longer references Playwright. The extension remains a browser adapter only; persisted Guide data and .NET Runtime retain Guide sequencing, validation/completion policy, capture, and progression ownership.

A fresh local build and extension/native-host verification is required before recording PASS for this restored baseline.

### Playwright removal from production Web runtime — 2026-10-06

The retired Playwright-based Web learner implementation has been removed from `DAP.Runtime.Web`. The production Web project no longer references the Microsoft Playwright package. Browser access for the production Web learner is exclusively through the restored browser extension, Native Messaging host, named-pipe adapter, and .NET Guide Runtime. Playwright must not be reintroduced as a production Web runtime dependency.
