# Architecture

This document defines the production architecture of DAP Platform.

## High-level structure

```text
DAP.exe (.NET 8 / WPF)
    |
    +-- Learner
    |
    +-- Editor
    |
    +-- DAP Core
          |
          +-- Guide model
          +-- Validation
          +-- Progress
          +-- Localization contracts
          |
          +-- Web Runtime -------- Microsoft Playwright for .NET
          |
          +-- Windows Runtime ---- Microsoft UI Automation
          |
          +-- Data abstractions
                  |
                  +-- SQLite
                  +-- Additional providers
```

## Repository structure

```text
src/
  DAP.App/
  DAP.Core/
  DAP.Data/
  DAP.Data.Sqlite/
  DAP.Runtime.Web/
  DAP.Runtime.Windows/

tests/
  DAP.Core.Tests/
  DAP.Web.Tests/
  DAP.Windows.Tests/
  DAP.Integration.Tests/

docs/
```

Dependencies must point inward toward Core abstractions.

## Desktop application

GUI technology: WPF on .NET 8.

One executable, DAP.exe, hosts both Learner and Editor experiences. Access to each mode can be controlled by roles/permissions.

The application orchestrates runtime transitions but does not implement Web or Windows target resolution itself.

## Shared guide model

Web and Windows steps use a common logical model.

A Step identifies its runtime and contains:

- Instruction/content.
- Target descriptor.
- Validation definition.
- Ordering and navigation metadata.

Runtime-specific target descriptors are interpreted by the corresponding runtime adapter.

## Web Runtime

The production Web Runtime uses Microsoft Playwright for .NET directly from the .NET application.

Python is not a deployment dependency.

Responsibilities include:

- Connect to/control the supported browser context required by DAP.
- Resolve Web targets.
- Render or coordinate guide UI for Web targets.
- Observe learner actions rather than automate them during normal guide execution.
- Validate actions/state.
- Re-resolve targets after DOM changes and navigation.
- Handle frames and browser context changes.
- Provide Web recording/target-capture capabilities required by Editor.

Browser/Playwright deployment dependencies must be packaged or validated explicitly by the product installer/startup process; they must not be left as an undocumented machine assumption.

## Windows Runtime

The production Windows Runtime uses Microsoft UI Automation.

Responsibilities include:

- Resolve native Windows targets.
- Track target bounds and lifecycle.
- Render/coordinate overlay guidance.
- Observe and validate learner interaction.
- Re-discover targets when applications/windows change.
- Provide Windows target-selection capabilities required by Editor.

## Data architecture

Core code depends on data abstractions, not on a specific database engine.

SQLite is the first provider. Additional providers can be added behind the same contracts.

## Localization

GUI localization is resource-based.

```text
Resources/
  Strings.en.resx
  Strings.he.resx
```

Changing GUI language changes both text and flow direction.

Guide-content language is independent of application GUI language.

## Deployment

Deployment is framework-dependent.

Prerequisite:

- .NET 8 Desktop Runtime installed on the target Windows machine.

DAP.exe does not require Python.

All other runtime dependencies required by Playwright/UIA integration must be handled or validated as part of the production deployment design.
