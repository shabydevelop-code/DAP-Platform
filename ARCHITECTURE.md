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

### Guide navigation and context

Guide navigation must not assume that the previous or next Step is renderable in the current application context.

A guide may cross page navigations, DOM replacements, browser contexts, Windows applications, or runtime boundaries such as Web -> Windows -> Web. Therefore, Step order and physical application navigation are separate concerns.

Each Step defines an advance mode:

- `AutomaticOnValidation` — the learner performs the required action and DAP advances only after validation succeeds.
- `Manual` — informational/non-action Step; the bubble exposes a Next action.

A global Previous button is not guaranteed. Previous may be exposed only when the runtime can determine that the previous Step is safely renderable in the current context. DAP must not implement Previous as an unconditional `StepOrder - 1`.

The Step model must carry sufficient context/navigation metadata for runtimes to determine whether a Step can be rendered in the current context. This rule applies equally to Web, Windows, and hybrid guides.

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
- Treat the frame path as part of Web target context. A target may live in a different iframe from surrounding application chrome, and both the frame and target must be re-resolved after refresh/replacement.
- Support multi-frame server applications where persistent header/navigation and active business content are hosted in separate iframes.
- Provide Web recording/target-capture capabilities required by Editor.

### Server-backed Web context rule

For server-backed Web applications, DAP must assume that any server round trip may refresh, replace, or rebuild the relevant page DOM while preserving the user's business context.

A preserved business context means the user may remain on the same logical record, tab, transaction, or process even though the DOM nodes that existed before the server call no longer exist. Context also includes relevant transient working state, such as unsaved form values, when the application performs its own server round trip and restores the same logical screen.

Therefore the Web Runtime must:

- Treat DOM element identity as transient across server calls.
- Never infer a context change solely from DOM replacement or re-rendering.
- Re-resolve the active Step target after a server response/refresh.
- Determine context from stable application signals such as URL/navigation state, record identifiers, page state, and runtime context metadata rather than retained DOM references.
- Keep the active guide Step attached to the same logical business context when that context survives the refresh.
- Distinguish persisted database state from transient working state. Application-triggered server refreshes may rebuild the document while restoring unsaved values; DAP must evaluate the post-refresh state actually presented to the user.
- Advance only when the Step validation succeeds; a server round trip or DOM refresh by itself is not completion.
- Support PeopleSoft-style flows in which a field action invokes the server, the page is refreshed/rebuilt, and the same record/context is restored.

This is the default design assumption for the Web Runtime's server-backed application mode, not a TestCRM-specific workaround.

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

## Permanent Web test target

`test-apps/DAP.TestCRM` is the repository's server-backed CRM target for integration and end-to-end testing. It is intentionally separate from DAP product runtime code.

The application models Customer -> Sites -> Cases / Leads and provides server-backed grids, record navigation, create/edit/save flows, asynchronous requests, DOM replacement, and separate header/content iframes. Clicking the header returns the content frame to the customer portal. Production Web Runtime contracts and guide-model requirements should be validated against this target rather than designed only from static examples.


## DAP.TestCRM PeopleSoft interaction contract

DAP.TestCRM must preserve a PeopleSoft-style server-backed interaction model as a permanent test constraint, not merely a visual style.

For operations that logically execute on the server, the expected flow is:

1. Capture the current logical context and transient working state.
2. Perform the server round trip.
3. Rebuild/reload the Content iframe/document as appropriate.
4. Restore the same logical context and unsaved working values when the operation does not intentionally navigate elsewhere.
5. Present the server result only after the refreshed context is established.

This rule applies consistently to search, grid sorting, save/update operations, server-side validation failures, and other server-backed actions.

Validation decisions and validation messages originate on the server. On validation failure, DAP.TestCRM must preserve the user's unsaved values across the server-style refresh and then present the server-returned message in the PeopleSoft-style modal.

Client-only SPA updates must not be introduced for server-backed operations when they would bypass this interaction model.


### Server interaction feedback

During a DAP.TestCRM server round trip, the current business content remains visible whenever possible. The application must not replace the content with a generic loading placeholder or visually blank the current screen merely because a server request is in progress.

While the request is active, DAP.TestCRM displays a compact activity indicator (spinner + "מעבד...") and prevents duplicate interaction as needed. When the server response causes the Content iframe/document to be rebuilt, no intermediate "טוען..." placeholder is shown.

After the refreshed business context is established, operation feedback is presented in that context: successful save operations may show a transient success message, while server validation and other failures use the server-returned message in the standard modal.

This behavior is part of the permanent PeopleSoft interaction contract and applies consistently to server-backed search, sorting, FieldChange, save/update, delete, validation, and similar operations.


## E2E execution modes and synchronization

The permanent DAP.TestCRM E2E suite supports two execution modes through the `DAP_E2E_MODE` environment variable:

- `fast` — default for validation. Artificial human-like cursor movement, typing delays, and demonstration pauses are skipped. TestCRM's artificial server-thinking delay is also bypassed for the E2E request. Real server, DOM, route, iframe, and validation readiness conditions remain enforced.
- `visual` — demonstration mode. Human-like cursor movement, typing delays, processing feedback, and the artificial server-thinking delay are retained.

The E2E runner uses a 5-second default Playwright timeout. Frame discovery polls every 100ms. Fixed delays must not be used as substitutes for actual application readiness; synchronization should use route, DOM, frame, server-state, or validation signals.

The representative PeopleSoft-Web workflow currently validates Customer -> Site -> Case -> Lead, dynamic Lead deletion, and Case deletion. During server-backed navigation, the active Content iframe and route readiness are re-resolved after replacement/rebuild.
