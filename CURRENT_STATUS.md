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

- Solution/project structure.
- WPF shell.
- Learner UI.
- Editor UI.
- Shared guide domain model.
- Data abstraction and SQLite provider.
- Web Runtime integration using Playwright for .NET.
- Windows Runtime integration.
- Recorder.
- Localization resources.
- Production deployment/bootstrap validation.
- Automated tests.

## Next milestone

The current ten-scenario E2E baseline is validated. Further expansion should be driven by concrete coverage gaps rather than adding scenarios for their own sake. Use these scenarios to derive production Web Runtime contracts before building the Editor.


### Validation coverage
The validated E2E baseline already includes a real CRM value-validation flow in the Case workflow: changing status to `סגורה` makes `closeReason` required; an attempted save without that value is rejected by server validation while unsaved Subject and Description values are preserved; supplying `closeReason` (`טופל`) then allows the save to succeed. This validation currently runs without DAP bubbles and is part of the underlying CRM/E2E behavior, not a bubble-specific test.


### Target resolution model
A target is represented by a runtime-neutral TargetDescriptor rather than a single selector. It identifies the target through a primary locator plus zero or more anchors/context constraints. Resolution must discover candidates, apply the anchors, verify uniqueness, and return an explicit ambiguous/not-found result rather than guessing. The descriptor also carries the runtime and frame context required by the corresponding Web or Windows adapter. This model is intended to support re-resolution after DOM changes, iframe replacement, grid rerender/reorder, layout shifts, target disappearance/reappearance, and equivalent Windows UI changes.
