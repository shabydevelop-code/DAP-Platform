# Architecture

## High-level structure

```text
DAP Desktop
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
          +-- Web Runtime -------- Playwright
          |
          +-- Windows Runtime ---- UI Automation
          |
          +-- Data abstractions
                  |
                  +-- SQLite
                  +-- Future providers
```

## Initial repository direction

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

The exact project split may evolve as implementation begins. Dependencies must continue to point inward toward Core abstractions.

## Desktop application

Initial GUI technology: WPF on .NET 8.

One executable hosts both Learner and Editor experiences. Access to each mode can later be controlled by roles/permissions.

The application orchestrates runtime transitions but does not implement Web or Windows target resolution itself.

## Shared guide model

Web and Windows steps must use a common logical model.

A Step identifies its runtime and contains:

- Instruction/content.
- Target descriptor.
- Validation definition.
- Ordering and navigation metadata.

Runtime-specific target descriptors are interpreted by the corresponding runtime adapter.

## Web Runtime

The Web Runtime is Playwright-based.

Responsibilities include:

- Resolve Web targets.
- Render or coordinate guide UI for Web targets.
- Observe learner actions rather than automate them during guide execution.
- Validate actions/state.
- Re-resolve targets after DOM changes and navigation.
- Handle frames and browser context changes.

## Windows Runtime

The Windows Runtime is based on Microsoft UI Automation.

Responsibilities include:

- Resolve native Windows targets.
- Track target bounds and lifecycle.
- Render/coordinate overlay guidance.
- Observe and validate learner interaction.
- Re-discover targets when applications/windows change.

## Data architecture

Core code depends on data abstractions, not on a specific database engine.

SQLite is the first provider. Future providers can be added behind the same contracts.

## Localization

GUI localization is resource-based.

Initial resources:

```text
Resources/
  Strings.en.resx
  Strings.he.resx
```

Changing GUI language changes both text and flow direction.

Guide-content language is independent of application GUI language.

## Deployment

Initial deployment is framework-dependent.

Prerequisite:

- .NET 8 Desktop Runtime installed on the target Windows machine.

A self-contained distribution may be added later without changing the core architecture.
