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


## Web architecture guardrail — Zero Playwright means one browser path

This is a hard architectural rule for all future Web work.

**Zero Playwright does not mean replacing Playwright with another browser-automation stack.**
The Web product and its Web E2E verification must use the same browser-access architecture:

```text
DAP Runtime / Web E2E driver
        ↕
Extension-facing protocol
        ↕
Native Host
        ↕
Browser Extension
        ↕
Browser DOM
```

Forbidden as an alternate Web control path:
- Microsoft.Playwright / Playwright;
- direct CDP / remote-debugging-port browser control;
- Selenium;
- Puppeteer;
- a private BrowserHarness that evaluates DOM or dispatches input through CDP;
- any second browser-control architecture used only by tests.

Allowed:
- launching Chrome/Edge as an OS process when needed;
- using the DAP browser extension and Native Messaging as the browser-control boundary;
- adding explicit E2E/test-driver commands to the same extension protocol, provided they do not change production learner semantics;
- OS-level cursor movement for Visual mode where this is part of the existing test UX.

The test harness may orchestrate servers, browser processes, DAP processes, data setup, timing, assertions, and cleanup, but browser navigation/DOM actions/element inspection must cross the DAP extension boundary rather than a separate automation technology.

Before implementing any Web change, verify it preserves this single-path rule. If a proposed solution introduces a second browser-control mechanism, stop and redesign before committing.

**Current correction:** any CDP-based BrowserHarness introduced during the Zero Playwright migration is temporary invalid work and must be removed/replaced before the milestone can be considered complete.


## Zero Playwright implementation correction — extension-native E2E

The invalid CDP-based E2E BrowserHarness has been replaced. The Web E2E runner now uses the same browser boundary as the product:

```text
Web E2E runner
  ↕ dap-web-e2e-v1 named pipe
Native Messaging Host
  ↕ Chrome/Edge Native Messaging
DAP Web Runtime extension
  ↕ extension content runtime
TestCRM DOM
```

Key rules/state:
- Chrome/Edge is launched as a normal installed browser profile; no temporary profile, remote debugging port, CDP, Playwright, Selenium, or Puppeteer is used.
- The DAP extension must already be installed/reloaded in that browser profile.
- E2E browser actions and DOM assertions are explicit test-driver commands routed through the DAP extension.
- Each run gets a unique `dap-e2e-session` token in the TestCRM tab URL.
- `DAP.exe` receives the same session through `DAP_WEB_SESSION_ID`, so learner-runtime commands and E2E actions target the same browser tab.
- The Native Host bridges both the production Runtime pipe and the E2E pipe; this is one browser-access architecture, not a second automation stack.
- Visual mode may still move the real operating-system cursor, while target lookup/action semantics remain extension-routed.
- The five-second timeout ceiling remains unchanged.

The Zero Playwright milestone is not complete until the extension-native 54-Step canonical run passes in Chrome and Edge and the required public run modes are verified.

## Product independence from the E2E Runner

The production Web learner must be fully functional when the E2E Runner does not exist.

The only production path is:

```text
Persisted Guide
    ↓
AdapterWebGuideRuntime / AdapterWebLearnerRuntime
    ↓
ExtensionWebBrowserAdapter
    ↓ Named Pipe / Native Messaging
Browser Extension
    ↓
Live browser DOM
```

The Runtime owns all learner semantics: active-Step state, context checks, target-resolution intent, validation decisions, completion-condition evaluation, runtime capture/materialization, and Step advancement.

The extension owns only browser-observable mechanics: frame routing, DOM resolution/observation, event capture, bubble presentation, and reporting facts/events to the Runtime.

The E2E Runner may use explicit test-driver commands through the same extension boundary to simulate learner actions and make assertions. Those commands must never become a source of facts or decisions required by production learner execution.

A hard acceptance rule applies: if a persisted Web Guide can complete only while the Runner is connected, the Web architecture is invalid regardless of E2E pass status.

Accordingly, the current product-first verification target is a Runner-free manual canonical Guide run with only TestCRM, DAP Learner Runtime, the persisted Guide database, and the installed production extension active. Automated extension-native E2E remains a regression layer after that autonomous path is proven.

## Runner-free development launch

`scripts/start-testcrm-web-autonomous.ps1` is the canonical development entry point for proving Web learner autonomy.

The script is startup orchestration only and terminates after launching the owned development processes. It does not use the E2E pipe, does not set `DAP_WEB_SESSION_ID`, and does not send browser test-driver commands.

After launch, the runtime topology is exactly the production topology documented above. A human performs the learner actions while the persisted Guide and production Runtime make every target, validation, completion, capture, and progression decision.

## Stable presentation and production-tab affinity

After a production application tab has been identified uniquely from persisted Guide semantics, the extension keeps that browser-tab identity for the active Native Messaging connection. Normal navigation, iframe replacement, and DOM changes inside the same application must not trigger a browser-wide tab scan on every learner reconciliation cycle.

Bubble presentation is also required to be idempotent and visually stable. Repeated reconciliation for the same Step/target must not hide and recreate the bubble. Short-lived target disappearance during DOM/server replacement receives a small grace window before presentation teardown; if the target remains absent the normal NotFound path applies, and if it is replaced the Runtime re-resolves and reattaches deterministically.

These optimizations do not weaken ambiguity handling or move Guide decisions into the extension.

## Verified stable-presentation baseline

The production Web learner has been manually re-verified PASS 54/54 after the presentation and transport-lifecycle optimizations.

Verified versions:
- extension manifest: `0.2.4`;
- content runtime: `0.4.3`.

The accepted runtime behavior is now:
- an unchanged Step/target keeps the same visible bubble instance;
- placement recalculation must not toggle visibility merely because reconciliation ran again;
- transient target/document replacement is tolerated and re-resolved deterministically;
- a uniquely identified application tab may be retained for the active connection to avoid browser-wide rescans;
- browser/application click semantics must execute normally before Runtime-owned presentation teardown;
- zero-match remains NotFound/inactive, multiple matches remain Ambiguous, and no optimization may introduce guessing.

This baseline is verified with the E2E Runner fully absent.

