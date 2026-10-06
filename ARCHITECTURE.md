# Architecture

This document defines the current production architecture of DAP Platform.

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
          +-- Web Runtime -------- Browser Extension Adapter + .NET Runtime
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
  DAP.Runtime.Web.Extension/
  DAP.Runtime.Web.NativeHost/
  DAP.Runtime.Windows/

test-apps/
  DAP.TestCRM/
    Server/
    Web/
    Windows/

tests/
  DAP.Data.Sqlite.Tests/
  DAP.TestCRM.E2E.Common/
  DAP.TestCRM.Web.E2E/
  DAP.TestCRM.Windows.E2E/
```

Production target resolution, validation, bubble presentation, completion rules, capture, and Guide progression belong to production Runtime code. Test projects may automate learner actions and assert outcomes but must not own production semantics.

## Desktop application

DAP is one Windows desktop application built with .NET 8 and WPF. Learner and Editor are separate product modes within the application architecture.

The application orchestrates runtimes but does not implement runtime-specific target resolution directly.

## Shared Guide model

Web and Windows use a shared logical Guide model.

A Step may contain:

- instruction/content;
- runtime;
- target descriptor;
- frame/context information;
- validation definition;
- completion conditions;
- capture definitions;
- advance behavior.

`TargetDescriptor` is persistence-independent and may contain multiple anchors. Resolution must return exactly one target, explicit NotFound, or explicit Ambiguous. The Runtime must never guess.

## Persistence

Core logic depends on data abstractions rather than a concrete database.

SQLite is the current provider. The default database is normally:

```text
C:\ProgramData\DAP\Data\DAP.db
```

`DAP_DATABASE_PATH` remains the supported explicit override.

Guide ownership rule:

**Seed initializes. DB owns. Runtime consumes.**

Normal execution must not silently overwrite an existing persisted Guide with the seed definition.

## Web Runtime

The Web learner has a single browser-access architecture:

```text
DAP.exe / .NET learner policy
    ↕
ExtensionWebBrowserAdapter
    ↕ Named Pipe
DAP.Runtime.Web.NativeHost
    ↕ Native Messaging
Manifest V3 service worker
    ↕ frame-scoped messaging
content-runtime.js
    ↕
Browser DOM
```

The .NET Runtime is the behavioral owner. The extension and Native Host are browser-access/transport infrastructure.

The .NET Runtime owns:

- active Step state;
- Guide sequencing;
- context decisions;
- validation decisions;
- completion-condition evaluation policy;
- runtime capture/materialization;
- Step progression.

The extension owns browser-observable mechanics:

- browser tab and frame routing;
- target resolution from descriptors supplied by the Runtime;
- DOM/browser event observation;
- content-script lifecycle;
- bubble rendering and placement;
- reporting browser facts/events to the Runtime.

No second browser-control architecture is permitted for production or Web E2E. Direct debugging-protocol control, Selenium, Puppeteer, or any test-only DOM-control path that bypasses the DAP extension boundary is prohibited.

Chrome and Edge are supported browser products. They are compatibility targets, not separate DAP execution modes.

The browser profile containing the installed DAP extension is discovered automatically. If no compatible installed profile contains the extension, execution fails explicitly. If multiple profiles match, execution fails as ambiguous rather than guessing.

## Web frame and context rules

Frame hierarchy is part of target context.

A missing `FrameContext` means the top-level document. A persisted frame path must be resolved explicitly and re-resolved after iframe/document replacement.

DOM identities are transient across server round trips, reloads, rerenders, and frame replacement. DAP preserves logical business context by evaluating current externally observable application state, not by retaining stale DOM references.

TestCRM intentionally exercises replacement/promotion of the Content iframe. Browsing-context names are not treated as stable frame identity; the live iframe element and frame path are resolved through extension-native mechanics.

## Web validation

Text/value validation is commit-based:

- text: real edit followed by blur;
- discrete control: natural change action;
- click: observed click event.

Invalid non-click commits are consumed. A new edit/change and new commit are required before reevaluation.

Valid commits may remain latched while persisted completion conditions are pending.

A validating click capable of browser-default navigation/submission must reach DAP before that default action is allowed to destroy the source document. DAP does not replace application event handlers.

## Bubble presentation

Target ownership and presentation surface are separate.

A target may belong to a constrained child frame while presentation is promoted to the top-level page. Promotion does not change target identity or validation ownership.

Bubbles expose an explicit drag handle. Manual placement becomes authoritative for the active Step and must not be overwritten by normal reconciliation.

When an attached target leaves the visible viewport, the bubble is hidden. If the target returns, presentation may resume from the same Step state.

Guide completion is explicit learner UI. Finishing the Guide does not imply closing the target browser/application.

## Windows Runtime

The Windows Runtime uses Microsoft UI Automation.

Responsibilities include:

- native target resolution;
- target geometry/lifecycle tracking;
- learner bubble presentation;
- learner action observation;
- validation;
- re-resolution after window/control changes.

Windows text validation follows the same commit principle as Web: a real edit followed by focus leaving the target. Discrete controls commit on their natural selection/change action.

Initial presentation may scroll a target into a comfortable visible region once. Reconciliation must not repeatedly override intentional learner scrolling.

## Closed-target rule

DAP must work with closed third-party target applications.

Production Runtime and Editor capabilities must not require target source code, internal databases, private APIs, or hidden implementation knowledge.

TestCRM source may be inspected during development for diagnosis, but any resulting fix must be generic and rely only on production-observable interfaces at runtime.

## Web E2E architecture

The Web E2E runner is regression automation, not part of learner semantics.

It may:

- launch owned application/DAP/browser processes;
- prepare fixture data;
- execute synthetic learner actions;
- inspect test results;
- move the OS cursor in Visual mode;
- clean up owned resources.

It must not:

- provide hidden target selectors that compensate for persisted Guide data;
- add hidden validation/completion logic;
- decide Step progression;
- become required for the production learner to work;
- bypass the extension for browser actions or DOM inspection.

The successful runner-free manual 54-Step Web Guide is the acceptance proof that the persisted Guide and production Runtime contain the necessary learner semantics.

## Canonical Web run modes

Public Web modes are:

```text
--manual
--guided --fast
--guided --visual
--manual-from-step N
--fast-from-step N
--visual-from-step N
--unguided
```

All seven modes have passed regression on the current Web baseline. Focused from-Step modes use the real preceding workflow as bootstrap rather than fabricating application state.

## E2E timing

The default E2E timeout ceiling is 5 seconds. Any increase above 5 seconds requires explicit user approval.

Current accepted Web learner reconciliation cadence:

- recovery state: 100 ms;
- stable resolved Step: 500 ms.

Manual measurement at 500 ms is approximately 2% idle CPU for the TestCRM Chrome tab.

## Process/output isolation

Canonical E2E runners build long-lived owned processes into unique per-run temporary directories under `%TEMP%\DAP\E2E`.

The runners must not kill unknown processes merely because they own a required port. Ports 5200 and 5201 are prerequisites for the canonical TestCRM Web run; if occupied by another process, startup fails explicitly.

## Localization

DAP-owned UI text is loaded from external JSON localization files shipped with the application.

```text
Localization/
  language.json
  he.json
  en.json
```

There are no compiled translation fallbacks. Missing configuration, language files, keys, or invalid direction are explicit configuration errors.

Guide instructional content remains persisted Guide data.

## Deployment

Target Windows machines are assumed to have the .NET 8 Desktop Runtime installed.

The Web deployment must also validate/install the DAP browser extension and Native Messaging host requirements.

Python is not a product dependency.

## Current verified Web baseline — 2026-10-06

- Persisted Web Guide: 54 Steps.
- Runner-free manual execution: 54/54 PASS.
- Guided Fast automated execution: 54/54 PASS.
- Browser control and DOM access use the extension/native-host architecture.
- Guide target identity, validation, completion, and progression remain owned by persisted Guide data plus the production Runtime.
- Stable idle Chrome tab CPU: approximately 2% at the accepted 500 ms stable reconciliation cadence.

## Web presentation readiness and scrolling

Presentation readiness distinguishes target existence, obstruction, and viewport position.

A resolved target that is rendered and enabled but lies outside the current viewport may continue to the presentation path. The learner then scrolls it into view before placing the bubble.

When a target is already inside the viewport, hit-testing may defer presentation while another application surface covers it. This avoids exposing the next actionable Step while the application is still covering that control.

This also avoids a circular dependency where an off-screen target would need to be visible before the presentation path responsible for scrolling it could execute.

Current verified extension baseline: **0.2.8**. The full runner-free 54-Step Manual Web regression and the complete seven-mode Web regression matrix passed on 2026-10-06.

Guide Steps may persist `AutoFocusTarget`. For value-entry Steps that enable it, the platform adapter applies focus when the target presentation is created; ordinary reconciliation must not repeatedly steal focus. SQLite persists this shared Guide property, while platform-specific runtimes implement the focus mechanic.
