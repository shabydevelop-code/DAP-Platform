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

## Historical validation

Earlier isolated experiments validated important Web runtime behaviors including browser interaction, frames, DOM replacement, server-side field changes, reloads, navigation, API-backed saves, target re-resolution, and grid interaction.

These results inform the product architecture but are not part of the production implementation and must not define production packaging or dependencies.

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

Create the production .NET solution structure and shared Core contracts, followed by the desktop shell and runtime interfaces.
