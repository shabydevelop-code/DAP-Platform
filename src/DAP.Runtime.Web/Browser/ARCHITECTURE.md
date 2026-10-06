# Web Browser Adapter

The Web learner has one behavioral owner: the DAP .NET Runtime.

The browser adapter is a platform and transport boundary only. It must not own Guide sequencing or invent alternate validation, context, reconciliation, capture, completion, or progression behavior.

## Single browser-access architecture

The Web product and Web E2E verification use one browser-access architecture:

```text
DAP .NET Runtime / Web E2E runner
        ↕
Extension-facing protocol
        ↕
Native Host
        ↕
Browser Extension
        ↕
Browser DOM
```

Production learner path:

```text
DAP.exe / AdapterWebGuideRuntime
    ↕
ExtensionWebBrowserAdapter
    ↕ Named Pipe
DAP.Runtime.Web.NativeHost
    ↕ Native Messaging
Manifest V3 service worker
    ↕ frame-scoped messaging
content-runtime.js
    ↕
DOM
```

There is no alternate browser-control stack for Web execution or Web E2E.

Forbidden as a second Web control path:

- direct browser debugging-protocol control;
- Selenium;
- Puppeteer;
- private browser harnesses that bypass the DAP extension boundary;
- test-only DOM control architectures that are unavailable to the production runtime.

Allowed:

- launching Chrome or Edge as normal OS processes;
- resolving the installed browser profile that contains the DAP extension;
- Native Messaging between the extension and DAP infrastructure;
- explicit E2E commands routed through the same extension boundary;
- OS-level cursor movement for Visual mode.

## Runtime ownership

The .NET Runtime owns:

- active Step state;
- Guide sequencing;
- context checks;
- validation decisions;
- completion-condition policy;
- runtime capture and materialization;
- Step progression.

The extension owns only browser-observable mechanics:

- browser/frame routing;
- DOM target resolution;
- DOM/browser event observation;
- content-runtime lifecycle;
- learner bubble presentation;
- reporting facts/events back to the Runtime.

The Native Host is a transport bridge. It is not a Guide engine.

## Target and frame semantics

- Missing `FrameContext` means the top-level document.
- Persisted frame paths are resolved explicitly and re-resolved after replacement.
- DOM element identity is transient across server round trips, reloads, rerenders, and iframe replacement.
- Target ambiguity is explicit and is never guessed.
- The Runtime must not depend on target-application source code, internal databases, or private APIs.
- Production target resolution is driven by persisted Guide data and generic runtime mechanics.

## Validation semantics

- Text validation commits on blur after a real edit.
- Discrete controls commit on their natural change action.
- Click validation is event-based.
- Invalid non-click commits are consumed and require another learner commit.
- Valid commits may remain latched while persisted completion conditions are still pending.
- A navigation-capable validating click must reach the Runtime before browser-default navigation is allowed to destroy the source document.
- Target/document replacement requires live re-resolution and validation rebinding.

## Bubble presentation

Presentation surface and target ownership are separate concerns.

A target may belong to a constrained child frame while its learner bubble is promoted to the top-level page. Promotion changes presentation only. Target identity and validation remain owned by the original resolved target and frame.

Manual dragging becomes authoritative for the active Step. Reconciliation must not overwrite an active/manual bubble position. The explicit drag handle uses `grab` on hover and `grabbing` for the full active drag lifetime.

A target that leaves the visible viewport hides its attached bubble; returning the target to the viewport makes the bubble eligible for presentation again.

## Extension lifecycle

The service worker probes content readiness before sending commands. Missing receivers are repaired by idempotent injection.

Extension reload may invalidate an older isolated world while the page remains alive. Calls from that stale context must fail safely; the next adapter command must be able to inject the current content runtime.

The extension transport uses named pipes and Native Messaging. Historical file-journal transport is not active.

## E2E architecture

The Web E2E runner is regression automation only. The production learner must remain fully functional without the E2E runner connected.

The runner may:

- start and stop TestCRM, DAP, and owned browser processes;
- prepare test data;
- execute synthetic learner actions;
- inspect test results;
- move the OS cursor in Visual mode;
- perform cleanup.

The runner must not:

- become a second source of Guide target identity;
- add hidden selectors that compensate for missing persisted target data;
- add hidden completion or validation rules;
- decide that a Step is complete;
- bypass the extension boundary for browser DOM actions.

If a Guide can complete only while the E2E runner is connected, the architecture is invalid.

## TestCRM frame rule

TestCRM intentionally replaces/promotes content iframes. Browser browsing-context names may become stale after that lifecycle. E2E frame routing therefore resolves the live iframe element through the same frame-path mechanics used by the extension rather than assuming `window.name` is stable identity.

## Timing rules

The default E2E timeout ceiling is 5 seconds. Increasing a timeout above 5 seconds requires explicit user approval.

Recovery-state reconciliation may use a shorter cadence than stable-state reconciliation. The current accepted Web learner cadence is 100 ms for recovery states and 500 ms for a stable resolved Step.

## Current verified baseline — 2026-10-06

Verified through the extension-native architecture:

- Runner-free manual persisted Guide: 54/54 PASS.
- Guided Fast persisted Guide: 54/54 PASS.
- Chrome extension profile is discovered automatically; there is no browser-mode selector.
- Stable idle Chrome tab CPU is approximately 2% at the accepted 500 ms stable reconciliation cadence.
- The persisted Guide database and Runtime are sufficient for target resolution, validation, completion, and progression; E2E must remain only the synthetic learner/action layer.
