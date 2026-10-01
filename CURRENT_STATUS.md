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
- WPF shell.
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

`DAP.Runtime.Web.Learner.WebLearnerRuntime` now provides the first production active-Step lifecycle. It reconciles the active Step every 100ms, always re-resolving from its `TargetDescriptor`; a replaced iframe/document or rerendered target is therefore reacquired instead of retaining stale Playwright identity. `WebBubblePresenter.EnsureShownAsync` is idempotent for the same Step and live DOM target, so reconciliation does not recreate/flicker the bubble on every pass. If the target is missing or ambiguous, the runtime hides stale guidance and continues waiting rather than guessing. Playwright navigation/frame races are retried by the next reconciliation cycle. Validation-driven Step completion/advance is intentionally not part of this lifecycle yet and remains the next production layer.

## Next milestone

The runtime-neutral target-resolution Core has now been started in `src/DAP.Core`. It defines `TargetDescriptor`, `Locator`, `Anchor`, `FrameContext`, runtime identity, and explicit `Resolved` / `NotFound` / `Ambiguous` resolution results. The Core has no Playwright, UIA, SQLite, or test-project dependency.

The target-resolver contract now exists in `DAP.Core`, and the initial Playwright-backed `WebTargetResolver` exists in `DAP.Runtime.Web`. The Web implementation re-resolves frame hierarchy per resolution request, filters candidates through all configured anchors, and returns explicit `NotFound` / `Resolved` / `Ambiguous` outcomes. Playwright remains outside Core. Initial anchor-relation evaluation currently supports CSS anchors; this is a deliberate first implementation boundary, not a Core limitation.

Next: add focused production resolver tests outside the E2E implementation, then introduce the shared `Validation`, `Bubble`, and `GuideStep` Core models before wiring bubble presentation to the Learner Runtime. Bubble rendering remains production runtime code and must not be implemented inside the E2E project. The existing ten-scenario E2E baseline remains the regression baseline.


### Validation coverage
The validated E2E baseline already includes a real CRM value-validation flow in the Case workflow: changing status to `סגורה` makes `closeReason` required; an attempted save without that value is rejected by server validation while unsaved Subject and Description values are preserved; supplying `closeReason` (`טופל`) then allows the save to succeed. This validation currently runs without DAP bubbles and is part of the underlying CRM/E2E behavior, not a bubble-specific test.


### Target resolution model
A target is represented by a runtime-neutral TargetDescriptor rather than a single selector. It identifies the target through a primary locator plus zero or more anchors/context constraints. Resolution must discover candidates, apply the anchors, verify uniqueness, and return an explicit ambiguous/not-found result rather than guessing. The descriptor also carries the runtime and frame context required by the corresponding Web or Windows adapter. This model is intended to support re-resolution after DOM changes, iframe replacement, grid rerender/reorder, layout shifts, target disappearance/reappearance, and equivalent Windows UI changes.
