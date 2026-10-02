# Current Status

## Verified four-mode persisted-Guide baseline — 2026-10-02

The current canonical execution modes are **Guided** and **Unguided**. The old `CRM-only` naming is historical and must not be used for the current mode contract.

Locally verified baseline:
- **Web Unguided — PASS 53/53.** Executes the 53 persisted Steps from `testcrm-web-canonical-workflow` through the E2E action executor without DAP.exe/bubbles.
- **Web Guided — PASS 53/53.** Executes the same persisted 53-Step Guide through DAP.exe, the production Web Runtime, real target resolution, validation, and learner bubbles.
- **Windows Unguided — PASS 10/10.** Executes the 10 persisted Steps from `testcrm-windows-canonical-workflow` through the Windows E2E action executor without DAP.exe/bubbles.
- **Windows Guided — PASS 10/10.** Executes those same 10 persisted Steps through DAP.exe, the production Windows Runtime, real UIA targets, validation, and learner bubbles.
- The separate Windows canonical 53-step business-flow harness is also locally verified PASS, but **DAP Windows Guided/Unguided persisted-Guide coverage is currently 10 Steps, not 53**.

Recent Windows Runtime findings/fixes now part of the verified baseline:
- Repeated/grid targets must resolve uniquely. The Windows Guide Step for the site row uses contextual identity rather than silently choosing the first matching `DataItem`: the target is scoped by the `SitesGrid` ancestor and the intended site identity.
- Multiple anchors are supported by the existing runtime-neutral `TargetDescriptor`; ambiguity remains an explicit resolution result and the Runtime must not guess.
- The persisted Guide is the source of learner bubble content. Guided E2E reads expected bubble content from the Guide loaded from `DAP.db` instead of duplicating instruction strings in the harness.
- Windows navigation actions synchronize on their rendered destination screen before the next learner action proceeds.
- A UIA target can exist before it has usable visible bounds. Windows Runtime reconciliation now waits until a resolved target is visible and has non-empty bounds before presenting its bubble instead of crashing.
- For Windows `clicked` validation, completion can be observed either through the invoke event or when a previously resolved/visible activated target leaves the UI during the resulting navigation/re-render. A target that has never resolved does not satisfy `clicked`.
- The Windows E2E action waits remain bounded by the 5-second policy; the fixes did not increase learner/action timeouts.
- Guide seed changes do not silently overwrite an existing persisted Guide. Test Guide updates are applied explicitly with `--reset-guide`, preserving the rule: **Seed initializes. DB owns. Runtime consumes.**

Next Windows milestone: expand `testcrm-windows-canonical-workflow` from the verified 10 persisted Steps toward the representative 53-step business workflow while preserving the four-mode baseline.


Last updated: 2026-10-02

## Verified baseline — 2026-10-02

- The canonical Web Guide `testcrm-web-canonical-workflow` contains 53 persisted Steps in `DAP.db`.
- Normal Web E2E is locally verified PASS for the full 53-Step Customer -> Site -> Case -> Lead workflow using `DAP.exe`, the production Web Runtime, real bubbles, validation, and persisted Guide data.
- Web `--crm-only` is locally verified PASS for the same canonical 53-Step workflow sequenced by the same persisted Guide, with `DAP.exe` and bubble synchronization intentionally omitted.
- CRM-only is not a separate TestCRM QA workflow. It is the same canonical guided business scenario executed without the learner presentation/runtime process.
- Repeated synchronization on the currently active Guide Step is valid when the harness first waits for a transition and then waits again immediately before that Step's learner action; the sequence guard rejects backward movement and skipped Steps.
- Temporary Step-2/backend diagnostics used during stabilization have been removed after both modes passed.
- The 5-second E2E timeout rule remains unchanged.

### Windows production-runtime milestone

- `src/DAP.Runtime.Windows` now exists as production code using Microsoft UI Automation.
- It includes production target resolution, WPF learner bubble presentation, Windows validation, and ordered Windows Guide execution.
- `DAP.exe --learner-windows <guide-key> --window-automation-id <id>` loads persisted Guide data and runs Windows Steps.
- The persisted Windows TestCRM Guide `testcrm-windows-canonical-workflow` currently contains 10 production-runtime Steps.
- All 10 persisted Windows Steps are locally verified PASS in both Windows Guided and Windows Unguided execution; Guided uses the real DAP Windows Learner Runtime and real UIA targets/bubbles.
- The separate Windows CRM-only canonical 53-step business scenario is also locally verified PASS. A focused test-only DB-backed executor has additionally proved that persisted Windows Guide Steps 1 -> 2 can drive CRM-only actions without DAP Runtime.
- The next Windows milestone is to expand the persisted Windows Guide/runtime coverage beyond Steps 1-2 toward the canonical 53-Step workflow. Instructor/Picker work remains deferred until Learner Runtime coverage is stable.

## Current phase

Web canonical 53-Step execution is stable in both Guided and Unguided modes. Windows persisted-Guide execution is stable for Steps 1-10 in both Guided and Unguided modes. Active work can now expand Windows persisted Guide coverage toward the canonical 53-Step business workflow.

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
- Recorder.
- Localization resources.
- Production deployment/bootstrap validation.
- Automated tests.


## Current implementation scope

Architecture/Core scope and active runtime implementation both cover Web + Windows. Web has the full persisted 53-Step canonical Guide baseline. Windows now has production UIA target resolution, WPF bubble presentation, validation, ordered Guide execution, and a locally verified persisted two-Step Learner path; expansion toward 53 Steps is next.

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


## Complete TestCRM learner Guide
- The first production-backed Web Guide now spans one complete business flow instead of only the initial search.
- Guide: customer search -> open customer -> open first site -> Cases tab -> create Case -> enter subject -> enter description -> save.
- The Guide contains 9 persisted Steps using production Core contracts and the production Learner Runtime.
- The E2E runner waits for the relevant production bubble before each guided action; it does not create or advance bubbles itself.
- The same Guide is therefore suitable for a future/manual learner run: automation is only acting as the learner.
- Existing broader CRM resilience scenarios continue after the Guide completes and remain independent of an active Guide.
- No TestCRM-specific validation kind was added; this flow is covered by the generic `clicked` and `value-not-empty` validations already owned by the Web runtime.




## Demo direction
- Current target is the polished visual TestCRM system demonstration, not a separate manual-run harness.
- The persisted 9-Step Guide remains the first guided business segment of that demonstration.
- Next bubble work should extend the visual demo only where guidance is meaningful, especially across real CRM server/DOM transitions, validation and business-state changes; avoid adding bubbles merely for test coverage.


## Demo Guide/action synchronization
- Visual demo invariant: while a Guide Step is active, the next visible learner action performed by the E2E must be exactly the action instructed by that Step.
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
- The full 53-Step visual run still requires local execution after pull to validate all dynamic selectors and frame transitions end-to-end.


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
- The previously verified 53-Step baseline predates this settling change; local visual E2E must be rerun after pull.


## Settling regression fix
- The first implementation of Web presentation settling caused the first bubble to time out because it attempted DOM identity comparison by passing an `ElementHandle` as an ordinary `EvaluateAsync` argument.
- The settling check now keeps the first resolved Locator and re-evaluates that original node after the settling window. A replaced/detached node fails through `isConnected` (or transient Playwright failure), while a fresh second resolution independently confirms the uniquely resolved target and stable geometry.
- Geometry is compared across the original node before/after the settling window and against the fresh resolution.
- No E2E timeout was increased; the fix addresses the settling implementation itself.
- Local 53-Step visual E2E must be rerun to verify this regression fix.


## Transition settling correction
- The prior target-identity settling approach was removed. Playwright `ILocator` is a live query and must not be treated as a frozen DOM-node identity token.
- The first Guide Step now bypasses transition settling because there is no preceding learner action/server transition to wait for.
- Subsequent Web Steps resolve their target and observe that target document for a quiet DOM window (default 250 ms). Subtree, child-list, attribute or text mutations reset the quiet timer.
- The quiet-window rule is independent of network activity: a local JavaScript rerender and a server-backed rerender are treated the same, while a genuinely quiet transition continues after the short window.
- At the end of the quiet window the target must still be connected and have non-zero geometry.
- This replaces both failed settling implementations that caused the first bubble to time out.
- Local 53-Step visual E2E remains required after pull.


## One-time Step presentation gate
- DOM settling is now a one-time gate before the first visible presentation of each Web Step, rather than a condition re-evaluated on every 100 ms reconciliation cycle.
- Step 1 bypasses the gate as before. Each subsequent Step waits for its quiet DOM window while no new bubble is created.
- Once a Step is successfully presented, normal `EnsureShownAsync` reconciliation owns it for the remainder of that Step; later rerenders no longer force the already-active Step back through settling.
- The settling-wait path no longer calls `HideAsync` repeatedly, eliminating the show/hide churn that amplified bubble flicker during rendering.
- If the target disappears in the small gap between passing the gate and its first presentation, the gate is reset and must pass again.
- Local 53-Step visual E2E and visual observation of server-render transitions remain required after pull.


## Guide/E2E target synchronization near end of visual flow
- A concrete Guide/E2E mismatch was found for the created Case: Steps 12 and 50 pointed the bubble at the first Case row, while the E2E visibly clicked the exact Case created during the run.
- TestCRM Case grid buttons now expose a semantic `data-business-subject` attribute in addition to their dynamic business ID.
- Steps 12 and 50 now target the created Case by the known business subject (`תקלה בחיבור לאינטרנט`) rather than by row position.
- The corresponding E2E clicks now use exactly the same semantic selector as the Guide. Before each click, the E2E asserts that the semantic target is unique and that its `data-business-id` equals the dynamically captured `createdCaseId`.
- This preserves deterministic business-record verification without embedding a runtime-generated ID into the persisted Guide and removes the known case where the bubble arrow and visual E2E cursor could point at different Case rows.
- The remaining late-flow Steps were reviewed against their visible E2E actions; Steps 41 and 48 intentionally use the first row on both sides, while breadcrumb/tab/delete/confirm/Header actions resolve the same logical controls.
- Local visual E2E should be rerun after pull, with particular observation of Steps 41-53.


## Full Guide/E2E compatibility audit
- All 53 persisted TestCRM Guide Steps were reviewed against the visible E2E workflow, including instruction intent, target selector, validation kind/value, context guard, frame and corresponding learner action.
- A second class of late-flow mismatch was found: several technical E2E assertions moved the visual cursor to re-resolved targets even though those cursor moves were not learner actions and had no Guide Step. The unguided moves to the Lead Delete target, selected-service field and switched-Case status field were removed; the underlying assertions remain non-visual.
- Direct visible actions that bypassed the common learner-action helpers were normalized: Step 41 now opens the first Lead through `Click`, Step 45 changes status through `Select`, and Step 49 returns through the same Site breadcrumb selector used by the Guide.
- `MoveTo` now enforces a runtime synchronization invariant before every visible learner action: the action target must be the exact DOM element stored as `__dapTarget` by the currently active production bubble. A bubble pointing to element A while the E2E cursor acts on element B now fails immediately instead of producing a misleading visual demo.
- This identity check works inside the target's own document, so it also covers Content-frame actions and the final Header-frame action.
- Together with the semantic created-Case target check, the visual E2E now verifies both business-record identity where required and exact bubble/action DOM identity for visible learner actions.
- The audit found no reason to turn technical assertions, measurements, readiness checks or the deliberate programmatic reload into Guide Steps; they remain non-visual E2E mechanics.
- A local full 53-Step visual run is required after pull to execute the new invariant against the complete workflow.


## Step 12 ambiguity found by full compatibility run
- The first local run after the compatibility audit timed out at Step 12. The semantic selector introduced for the created Case used subject `תקלה בחיבור לאינטרנט`, but TestCRM seed data already contains a Case with that exact subject.
- The resulting two matches correctly produced an ambiguous target; DAP did not guess and therefore did not present Step 12.
- The E2E-created Case now uses the unique stable business subject `תקלה בחיבור לאינטרנט - בדיקת DAP` throughout creation, post-close restoration, assertions and Steps 12/50 semantic selectors.
- This preserves the intended resolver invariant: Guide targets must resolve uniquely without relying on row position or injecting the runtime-generated Case ID into a Guide that was loaded before creation.
- Full 53-Step local rerun remains required; the new exact DOM identity invariant will then validate each visible action against its active bubble target.


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
- Full 53-Step local visual run is required after pull.


## Persistent TestCRM Guide database
- The visual TestCRM E2E no longer creates a random `%TEMP%\DAP.TestCRM.E2E\<guid>.db`.
- It resolves DAP's real database through `SqliteDatabaseOptions.CreateDefault()`, seeds/updates Guide `testcrm-create-case` there, reloads the 53 Steps from that repository, and launches DAP.exe with the exact same database path.
- Default path on Windows: `C:\ProgramData\DAP\Data\DAP.db`. `DAP_DATABASE_PATH` still overrides it explicitly.
- The run prints `DAP persistent guide database: <path>` so the inspected file is unambiguous.
- Dedicated SQLite repository tests continue to use isolated temporary databases; this change applies to the visual system demonstration.


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

## Manual Learner Run — 2026-10-01

- Added `scripts/run-testcrm-learner.ps1` for a real manual learner walkthrough of the persisted TestCRM Guide.
- The launcher starts TestCRM, opens Chrome by default with an isolated temporary browser profile and CDP enabled, waits for the browser endpoint, and starts the production `DAP.App --learner-web testcrm-create-case` path against that browser.
- The launcher performs no Guide actions. There is no E2E Click/Fill/Select automation in this mode; the human learner must perform every action and production validation drives all Step advancement.
- The same persistent DAP database/Guide ownership rules apply. The launcher does not seed or rewrite the Guide.
- Edge can be selected with `-Browser edge`; Chrome is the default.
- Manual command from repository root: `powershell -ExecutionPolicy Bypass -File .\scripts\run-testcrm-learner.ps1`.



### Focused manual learner runs
- The manual TestCRM learner launcher supports `-StartStep <order>` for focused UX/debug runs without replaying the whole Guide.
- DAP still loads the complete persisted Guide from SQLite; the requested order only selects where execution begins, so progress remains the original Guide position (for example, Step 53 of 53).
- This is a diagnostic entry point, not synthetic Guide state. Steps that depend on runtime values captured by earlier Steps still fail explicitly if those values are unavailable.
- The E2E Guide synchronization now requires the active bubble to be visibly rendered, not merely present in the DOM.

- For integration-state UX debugging, the representative TestCRM E2E supports `--manual-from-step 53`: automation follows the real Guide through Step 52, waits until the production Step 53 bubble is visible, then hands control to the tester while browser and DAP remain alive. This is preferred over synthetic state reconstruction when the Step depends on the complete prior workflow.


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
- Full normal automated 53-Step E2E has not yet been re-run after this complete UX/drag-handle sequence; the earlier cross-browser baseline remains historical until that regression run is repeated.

## GitHub repository access

- Canonical repository: `shabydevelop-code/DAP-Platform`.
- Connected GitHub account `shabydevelop-code` has verified repository permissions: `admin=true`, `maintain=true`, `pull=true`, `push=true`, `triage=true`.
- In new chats, do not assume repository access is read-only. When write capability matters, verify permissions from repository metadata before concluding that write access is unavailable.
- DAP development may read and write this repository through the connected GitHub tools unless verified repository permissions change.


## TestCRM refactor verification and database separation (2026-10-02)
- The TestCRM refactor to `Server/`, `Web/`, and `data/` was pulled and verified locally.
- TestCRM starts successfully on `http://localhost:5200` after the WebRoot fix.
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
- Local build/runtime verification is still required after pull; GitHub-side editing cannot execute the user's local Windows/WPF runtime.

## Web E2E unified modes and Windows parity review — 2026-10-02
- The representative Web E2E project is now `tests/DAP.TestCRM.Web.E2E/DAP.TestCRM.Web.E2E.csproj`.
- Web E2E execution uses one canonical Customer -> Site -> Case -> Lead business flow. Mode switches must not create a second independently maintained business scenario.
- `--crm-only` runs that canonical CRM flow without initializing/reading DAP.db, without loading the Guide, and without launching DAP.exe. This CRM-only baseline was locally verified through the terminal PASS.
- The normal Fast + DAP path was locally re-verified after the unified-mode refactor: the persisted Guide loaded 53 Steps and the complete representative workflow ended in PASS.
- `--manual-from-step <N>` preserves its existing semantics: execute the real preceding workflow in Fast mode, wait until production Guide Step N is visibly ready, then stop automation and hand control to the human tester. Step 47 handoff was locally verified.
- Added `--visual-from-step <N>`: execute Steps before N in Fast mode, switch the same running canonical scenario to Visual at Step N, and continue automatically to the end. `--manual-from-step` and `--visual-from-step` are mutually exclusive.
- Local verification of `--visual-from-step 47` showed the explicit `FAST -> VISUAL` transition at Step 47 and completed the workflow with terminal PASS.
- Visual frame-replacement waiting no longer requires observing the transient `#content-frame-next` Attached state. The harness waits for the stable replacement outcome/current ready Content frame, avoiding a race where the transient frame can be created/promoted before Playwright observes it. The Step-47-to-end Visual verification passed through the previously failing Step 50.
- In full Visual mode the final Step 53 bubble is intentionally left visible briefly before the automated final action so the final Guide instruction can be observed.
- Current Web execution behaviors are therefore: full Fast, full Visual, Fast -> manual at N, Fast -> Visual at N, CRM-only, full manual Learner launcher, and explicit Guide reset. Browser selection remains `chromium|chrome|edge` where applicable.
- Windows TestCRM was reviewed against the persisted 53-Step Web Guide. Most of the business workflow is relevant to Windows because both clients use the same server/API/business model, but Web-specific mechanics (DOM/iframe/frame URL/CSS targeting) must not be copied literally into Windows UIA tests.
- Known Windows parity gaps before building the Windows CRM-only E2E: Case Resolution Notes is displayed/enabled but is not currently persisted in `CaseInput`; there is no Windows equivalent of the Web Step-15 activity-more interaction; and the Web Step-6 explicit Cases sort behavior does not currently have an equivalent explicit Windows implementation.
- Windows validation/delete confirmations currently use WPF `MessageBox`, which is a valid platform-specific equivalent rather than the Web PS alert/confirm DOM. Dynamic Lead status behavior and conditional Selected Service UI are present and are suitable for equivalent Windows business-flow coverage.
- Planned order remains: close required Windows CRM parity gaps -> build a Windows CRM-only UI Automation E2E against the real WPF client -> stabilize/PASS the business scenario -> only then integrate DAP Windows Runtime/target resolution/bubbles.



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


## Windows CRM-only canonical E2E baseline — 2026-10-02
- The Windows CRM-only UI Automation E2E is implemented in `tests/DAP.TestCRM.Windows.E2E` and executes the shared canonical core scenario from `tests/DAP.TestCRM.E2E.Common` against the real WPF TestCRM client.
- Local verification reached terminal PASS: `PASS: Windows CRM-only canonical Customer -> Site -> Case -> Lead core scenario completed.`
- The verified core covers Customer -> Site -> Case -> Lead navigation, Case creation/save/status FieldChange behavior, Resolution Notes enablement, required-field validation handling, Lead creation/save/status transitions, conditional Selected Service handling, Lead deletion confirmation, and return to Portal.
- WPF ComboBoxes used by the scenario expose a custom UIA ValuePattern through `AutomationComboBox`, allowing deterministic value selection without physical popup interaction.
- Case/Lead FieldChange responses are applied locally in the WPF client instead of immediately reloading the persisted record, preventing transient status changes from being reverted before Save.
- Windows E2E navigation now synchronizes on the rendered destination screen rather than assuming an async click has completed. Site breadcrumb targeting uses stable UIA identity (`AutomationId=Breadcrumb` plus the site name), and Lead creation retries safely until the form is ready.
- Modal WPF MessageBoxes are handled as real platform dialogs. Expected validation/delete dialogs are dismissed/confirmed explicitly; unexpected informational OK dialogs block further E2E actions until dismissed so the scenario cannot continue behind a modal window.
- Default Windows E2E wait timeout is 5 seconds.
- The activity-more interaction remains a Windows-specific UI area to align with the Web behavior; the canonical scenario no longer incorrectly calls validation dismissal immediately after `ShowMoreActivity`.
- This PASS establishes the Windows CRM-only core baseline. It does not yet mean that the complete persisted 53-Step Web Guide has been reproduced in Windows; the next parity work is to extend the shared/core coverage toward the remaining canonical business steps before integrating DAP Windows Runtime bubbles/target resolution.

- Windows/Web activity-more parity correction: Web renders `#activity-more` but defines no click handler or alert for it. The Windows-only `MessageBox` ("אין פעילויות נוספות להצגה.") was therefore removed; `ActivityMoreButton` now has the same no-alert/no-op business behavior as Web. Commit baseline follows the already verified Windows core PASS; local rerun is required after pull.
- Pending Windows parity milestone remains explicit: expand the current Windows CRM-only shared core scenario to cover the complete canonical 53-step business workflow before DAP Windows Runtime bubble/target integration is considered complete.

- **Windows canonical 53-step CRM-only milestone: PASS (locally verified 2026-10-02).** `CanonicalCrmScenario.Run53Async` completed the full Customer -> Site -> Case -> Lead flow and terminated with `PASS: Windows CRM-only canonical 53-step Customer -> Site -> Case -> Lead scenario completed.` The Windows E2E now synchronizes async sort/create/delete/navigation transitions in the test harness. Test-only row AutomationIds were removed from the Windows target application; E2E target resolution remains the responsibility of the test harness. No full-53 PASS is claimed for DAP Runtime/bubbles yet; this milestone is the CRM-only Windows business-flow baseline.


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
- Next planned runtime milestone: Windows Learner Runtime, starting with persisted Windows Steps 1–2, production UIA target resolution, and real learner bubbles using the same DAP database architecture.
