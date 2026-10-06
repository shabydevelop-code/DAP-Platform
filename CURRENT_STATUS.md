# Current Status

This document contains only the current implementation state. Historical milestones and retired runner modes are intentionally excluded.

## Current baseline

- .NET 8 production architecture is implemented for Web and Windows Learner runtimes.
- SQLite is the current persistence provider behind provider-independent Core/Data contracts.
- Default DAP database: `C:\ProgramData\DAP\Data\DAP.db`.
- TestCRM uses its own local business database: `test-apps\DAP.TestCRM\data\testcrm.db`.
- Current Web and Windows TestCRM Guide seeds contain 55 persisted Steps.
- Step 55 is the persisted centered Guide summary.
- Disabled persisted Steps are supported without renumbering.
- Persisted `AutomationValue` is supported for Hybrid value-entry actions.

## Web

Production Web Runtime no longer depends on Playwright.

The active production path is:

```text
DAP.exe
-> AdapterWebGuideRuntime
-> ExtensionWebBrowserAdapter
-> Native Host
-> Browser Extension
-> target DOM
```

The Native Host and unpacked Extension are implemented. Chrome Native Messaging registration has been verified during development.

The Web E2E runner uses the Extension test-driver channel for Hybrid synthetic actions. Runtime remains the sole owner of completion and Step progression.

## Windows

Production Windows Runtime uses Microsoft UI Automation.

A complete human Manual run has demonstrated persisted-data-driven learner execution without a parallel test completion engine. Hybrid uses persisted automation values only for configured value controls and synchronizes with Runtime progression.

## Canonical runner contract

Current public modes:

```text
--manual
--hybrid
```

Maintenance/path options:

```text
--reset-guide
--published-dap
```

Retired modes and mechanisms must not be reintroduced: Guided, Unguided, Fast, Visual, Manual-From-Step, Visual-From-Step, `DAP_E2E_MODE`, Playwright production execution, CDP production attachment, or the retired shared canonical E2E scenario.

## Recent cleanup

- Production Playwright Web implementation removed.
- Web production composition moved to Extension + Native Messaging.
- Web E2E BrowserHarness removed in favor of the Extension test-driver channel.
- Retired `DAP.TestCRM.E2E.Common`, `CanonicalCrmScenario`, and `PersistedWindowsCrmGuideExecutor` removed.
- Repository ignores local build outputs and TestCRM database files.
- Documentation consolidated at repository root and rewritten around the current architecture.

## Verification state

The Web E2E project and DAP application were built successfully after the Extension/Hybrid refactor. A fresh build of Web E2E, Windows E2E, and DAP.App should be run after the latest cleanup before runtime verification.

The next runtime verification sequence is:

1. Web Manual.
2. Web Hybrid.
3. Windows Manual.
4. Windows Hybrid.

Do not claim a fresh 55/55 automated regression until such a run has actually been completed.
