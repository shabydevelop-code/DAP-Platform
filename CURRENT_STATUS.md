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
- Product Learner launch uses `DAP.exe --learner --guide <GuideId>`; the runtime is selected from the persisted Guide. The current launcher accepts one enabled target runtime type per Guide.
- The browser Extension is infrastructure-only and has no popup GUI.

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

The Web E2E runner uses the Extension test-driver channel for Hybrid synthetic actions. Manual and Hybrid each use a dedicated Extension browser-session identity so intentional browser closure is detected independently of the Chrome launcher process and ends the run cleanly. Manual sends no synthetic learner actions. Runtime remains the sole owner of completion and Step progression.

## Windows

Production Windows Runtime uses Microsoft UI Automation.

A complete human Manual run has demonstrated persisted-data-driven learner execution without a parallel test completion engine. Hybrid uses persisted automation values only for configured value controls and synchronizes with Runtime progression. Runtime applies one-time initial input focus before Hybrid value actions, and intentional target-window closure ends the runner cleanly.

## Canonical runner contract

Current TestCRM execution contract:

```text
--manual --guide <GuideId>
--hybrid --guide <GuideId>
```

The supplied persisted Guide ID is loaded by the runner and passed unchanged to the product Learner. The runner does not select the Guide implicitly.

Maintenance/path options:

```text
--reset-guide
--published-dap
```

## Independent Web application and Learner execution

`tests/DAP.TestCRM.Web.Host` starts the TestCRM backend and Web application independently of DAP. It builds the application projects before starting a five-second Web readiness check, and keeps its owned TestCRM processes running until the host is stopped. The user opens `http://localhost:5200` in Chrome separately.

In a separate shell, `dotnet build src\\DAP.App\\DAP.App.csproj` builds the product; `src\\DAP.App\\bin\\Debug\\net8.0-windows\\DAP.exe --learner --guide testcrm-web-canonical-workflow` launches the Learner directly, without the E2E runner. DAP attaches to the already-open CRM through its persisted Web Application Context; it does not own the site or browser lifetime.

The independent Web Manual workflow has been verified through completion of the 55-Step Guide: the DAP process exited at Guide completion while TestCRM and Chrome remained running. This does not constitute a new Hybrid or Windows regression result.

## Verification state

Current Manual/Hybrid behavior is persisted-data-driven on both runtimes. Windows Hybrid input focus synchronization is verified for the first input Step. Intentional Windows target closure and Web browser-session closure terminate cleanly rather than being reported as Runtime failures. Web Hybrid emits the browser-session closure message once through its owning termination path; Web Manual retains its passive clean-closure path. Web Manual browser-lifetime observation is passive and does not automate learner actions.

Do not claim a fresh full-guide regression unless such a run has actually been completed.
