# Current Status

Last updated: 2026-09-30

## Current phase

Repository initialization and architecture definition.

## Confirmed product requirements

- Single Windows desktop application.
- Learner and Editor modes.
- Hebrew and English GUI with RTL/LTR support.
- Web and Windows guide support.
- Hybrid Web/Windows guides.
- Database-independent data layer.
- SQLite as the first database provider.
- .NET 8 Desktop Runtime is assumed on target machines.

## Proven before repository initialization

A standalone Playwright proof of concept successfully completed a 20-step interactive CRM guide flow covering browser interaction, iframe content, DOM replacement, server-side field changes, reloads, navigation between screens, API-backed saves, re-resolution of targets, and grid sorting.

This proof is reference knowledge only. POC code has not yet been migrated into DAP-Platform.

## Not implemented yet

- Solution/project structure.
- WPF shell.
- Learner UI.
- Editor UI.
- Shared guide domain model.
- Data abstraction and SQLite provider.
- Web Runtime integration.
- Windows Runtime integration.
- Recorder.
- Localization resources.
- Automated tests.

## Next milestone

Create the initial .NET solution structure without migrating legacy GWTP code. Establish the shared Core contracts first, then the desktop shell and runtime interfaces.
