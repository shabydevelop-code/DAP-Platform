# Web Browser Adapter

The Web learner has one behavioral owner: the DAP .NET Runtime.

The browser adapter is a platform/transport boundary only. It must not own Guide
sequencing or invent alternate validation, context, reconciliation, capture, or
bubble-completion behavior.

## Implementations

- `PlaywrightWebBrowserAdapter` — proven behavioral/regression implementation.
- `ExtensionWebBrowserAdapter` — production browser implementation used by
  `DAP.exe --learner-web`.

Both implementations expose the same `IWebBrowserAdapter` contract so learner
policy remains independent from the browser-access technology.

## Production extension transport

```text
AdapterWebGuideRuntime / AdapterWebLearnerRuntime
    ↕
ExtensionWebBrowserAdapter
    ↕ Named Pipe
DAP.Runtime.Web.NativeHost
    ↕ Native Messaging
MV3 service worker
    ↕ frame-scoped messaging
content-runtime.js
    ↕
DOM
```

The Native Host is a bridge. It is not a second runtime and must not own Step
state or business validation.

The old `commands.jsonl` / `responses.jsonl` / `events.jsonl` bridge is not
the active transport.

## Migration rule

Existing Playwright behavior is the specification.

In particular:

- a missing `FrameContext` means the top-level document;
- a persisted frame path is resolved explicitly and re-resolved after replacement;
- target ambiguity is explicit and never guessed;
- text validation commits on blur after a real edit;
- discrete controls commit on change;
- click validation is event-based;
- navigation/submission capable clicks use acknowledgement before replaying the
  browser's default action;
- invalid non-click commits are consumed;
- valid non-click commits remain latched while completion conditions are still pending;
- target/document replacement requires live re-resolution and validation rebinding;
- promoted top-level bubbles change presentation surface only, not target or
  validation ownership;
- reconciliation must not overwrite active/manual bubble dragging;
- active dragging keeps the `grabbing` cursor until release/cancel.

The extension reports browser facts/events to the .NET Runtime. The .NET Runtime
decides whether a Step completes and when the next Step starts.

## Lifecycle rules

The service worker probes content readiness before sending production commands.
If a receiver is missing, it injects the current content runtime and retries.

An unpacked-extension reload can leave an old content-script isolated world alive
while invalidating its extension context. Calls from that stale world must fail
safely; the next adapter command must be able to inject the fresh runtime.

The 5-second adapter timeout policy remains unchanged. Migration failures are not
resolved by increasing the timeout without an explicit architectural decision.

## Current verification status — 2026-10-05

The persisted 54-Step TestCRM Web Guide has completed manually end-to-end through
`ExtensionWebBrowserAdapter`.

Playwright remains in the repository as a regression/compatibility adapter until
the required automated extension-backed parity matrix is complete. This milestone
does not claim repository-wide Playwright removal.


## Web Zero-Playwright milestone — 2026-10-05

The single active Web milestone is now **Zero Playwright**.

Definition of done:
- no active `Microsoft.Playwright` package dependency in Web Runtime or Web E2E projects;
- no active `Playwright.CreateAsync()`, `IPage`, `IFrame`, `ILocator`, or equivalent Playwright browser-control code in the Web execution path;
- `DAP.exe --learner-web` continues to use the browser-extension adapter path;
- the Web E2E/manual harness also uses the extension/browser-native path rather than a separate Playwright browser-control stack;
- the existing public Web run modes remain available with the same intent: Guided Fast Full, Guided Visual Full, Manual From Step, Unguided Full, Visual From Step, and Manual Full;
- Chrome and Edge remain supported;
- the persisted 54-Step canonical Web Guide continues to pass through the unified extension-based architecture;
- production and development/test environments do not diverge into separate Web browser architectures.

This milestone replaces smaller intermediate migration goals. Playwright may remain only as historical/reference code until removed during completion of this milestone; it is not an acceptable steady-state dependency for Web Runtime or Web test execution.
