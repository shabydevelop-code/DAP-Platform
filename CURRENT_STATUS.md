# Current Status

Last updated: 2026-09-30

## Current phase

Production repository initialization and implementation planning.

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

Run and validate DAP.TestCRM locally, then use concrete Learner/runtime scenarios against it to derive the production Web Runtime contracts and shared guide model before building the Editor.
