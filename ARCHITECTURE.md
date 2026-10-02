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

Current implemented production/test projects:

```text
src/
  DAP.App/
  DAP.Core/
  DAP.Data/
  DAP.Data.Sqlite/
  DAP.Runtime.Web/

test-apps/
  DAP.TestCRM/

tests/
  DAP.Data.Sqlite.Tests/
  DAP.TestCRM.E2E/

scripts/
  run-testcrm-learner.ps1
```

`DAP.Runtime.Windows` and broader Windows/UIA test projects belong to the target architecture but are not implemented in the current repository baseline. Dependencies must continue to point inward toward Core abstractions.

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

### Bubble presentation surface vs target surface

The document/frame that owns a resolved Web target is not necessarily capable of displaying the learner bubble. A constrained child iframe may correctly contain the target while physically clipping any bubble positioned outside its small viewport.

Target identity and validation remain bound to the original resolved element and frame. Presentation alone may be promoted to the top-level page when the owning child frame cannot display the bubble. The current Web Runtime represents this promoted surface with `#dap-guide-bubble-proxy`.

Promotion is a generic presentation mechanism, not a target-resolution change and not a TestCRM/Step-specific workaround. A promoted bubble remains a real learner surface: it is interactive, must not pass clicks through to the underlying target, and follows the same learner interaction contract as a normal bubble.

Regular, promoted, and completion bubbles expose an explicit `⠿` drag handle. Only the visible handle starts dragging. Normal bubble content uses the default cursor, handle hover uses `grab`, and active dragging uses `grabbing`.

Guide completion is presented as a top-level completion bubble with an explicit `סיום` action. The Runtime waits for the learner's real click. Finishing the Guide ends the Guide/runtime flow but does not imply closing the target business browser.

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

### Bubble visual theme

Bubble content and per-step placement are guide data. The product's default visual language (colors, typography, border, radius, spacing, shadow, and equivalent presentation defaults) is runtime/theme configuration and is not duplicated in each GuideStep or persisted as per-step SQLite data.

The Web Runtime currently exposes this through a central `WebBubbleTheme`, consumed by `WebBubblePresenter`. This keeps presentation policy separate from guide content and persistence. A future Windows Runtime should map the corresponding product theme to its native presentation technology rather than storing Web CSS in Core or in the guide database. Per-guide/per-step visual overrides should be introduced only if they become an explicit product requirement.

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
SQLite default database location on Windows is `%ProgramData%\DAP\Data\DAP.db` (normally `C:\ProgramData\DAP\Data\DAP.db`). The application/provider may override this with `DAP_DATABASE_PATH`; Core must not depend on either the path or SQLite.


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

The representative PeopleSoft-Web workflow currently validates Customer -> Site -> Case -> Lead, dynamic Lead deletion, and Case deletion. The permanent E2E baseline now contains ten validated scenarios covering frame replacement, validation/state preservation, rerender/re-resolution, reload/context preservation, tab switching, target disappearance/reappearance, cross-frame navigation, layout shift, consecutive server updates, and business-context isolation. During server-backed navigation, the active Content iframe and route readiness are re-resolved after replacement/rebuild.


### Target resolution model
A target is represented by a runtime-neutral TargetDescriptor rather than a single selector. It identifies the target through a primary locator plus zero or more anchors/context constraints. Resolution must discover candidates, apply the anchors, verify uniqueness, and return an explicit ambiguous/not-found result rather than guessing. The descriptor also carries the runtime and frame context required by the corresponding Web or Windows adapter. This model is intended to support re-resolution after DOM changes, iframe replacement, grid rerender/reorder, layout shifts, target disappearance/reappearance, and equivalent Windows UI changes.


## DAP.exe process boundary

`src/DAP.App` is the production Windows executable host (`AssemblyName=DAP`, .NET 8 WPF). It is the composition root for persistence and runtime services. Learner bubble lifecycle belongs inside this process; there is no separate Bubble.exe.

The canonical guided Web E2E launches the production `DAP.exe` process, which attaches to the E2E-owned Chromium instance over CDP and owns Web Guide/bubble runtime behavior. CRM-only intentionally does not launch `DAP.exe`.

An independent DAP.exe cannot consume an `IPage` created inside another process. Production Web execution therefore requires DAP.exe to own or explicitly attach to a browser/Playwright connection. `DAP.exe --learner-web <guide-id> --cdp <endpoint> [--page-url-contains <text>]` now attaches to an existing Chromium browser through Playwright's CDP connection, selects exactly one matching page, loads the guide from the configured data provider, and starts its first Web Learner Step. Ambiguous page selection fails explicitly rather than guessing. The canonical E2E harness uses this external `DAP.exe` process boundary.


## Guide persistence identity

Persistence distinguishes database identity from runtime/business identity:

- `Guides.Id` and `GuideSteps.Id` are numeric internal database primary keys.
- `GuideSteps.GuideId` and `TargetAnchors.GuideStepId` are numeric foreign keys.
- `Guides.Key` and `GuideSteps.Key` are stable human-readable textual identifiers.
- Runtime launch and repository boundaries address Guides by textual key; numeric row IDs remain an internal persistence concern.
- Legacy SQLite databases that used textual primary keys are migrated in place by the database initializer.

This separation allows display names and stable textual keys to evolve independently from relational database identity and supports future Instructor-created Guides without exposing persistence IDs to runtime contracts.


## Self-contained TestCRM Web E2E topology

The normal TestCRM Web E2E runner owns the complete temporary test topology for its run:

1. Start `DAP.TestCRM.Server` on `http://localhost:5201`.
2. Start `DAP.TestCRM.Web` on `http://localhost:5200`, configured to use the shared backend.
3. Wait until the Web host is reachable.
4. Start the browser and DAP runtime path.
5. Execute the canonical scenario.
6. Terminate and dispose only the backend, Web host, and DAP processes created by the runner.

A normal full Web E2E run therefore does not require manually pre-started TestCRM servers.

The canonical Web Guide is `testcrm-web-canonical-workflow` and contains 53 persisted Steps. The full Web workflow was reverified after the numeric-ID persistence migration on 2026-10-02.

## Canonical Web guided vs CRM-only topology

The Web E2E has one canonical 53-Step business sequence backed by the persisted Guide `testcrm-web-canonical-workflow` in `DAP.db`.

Guided execution is: `DAP.db -> Guide Steps -> DAP.exe -> DAP.Runtime.Web -> target/bubble/validation -> TestCRM Web -> TestCRM Server -> testcrm.db`.

CRM-only execution is: `DAP.db -> Guide Steps -> E2E CRM action harness -> TestCRM Web -> TestCRM Server -> testcrm.db`.

CRM-only exists to run the same canonical CRM flow without learner bubbles. It must not evolve into a divergent TestCRM-specific QA path. Production Guide data owns the Step sequence, target semantics, validation, and context; the E2E harness owns only synthetic user actions/values needed to exercise those semantics.

## Windows Runtime topology

`DAP.Runtime.Windows` is a production adapter behind the shared Core model. It resolves Windows `TargetDescriptor` data through UI Automation, presents non-activating WPF learner bubbles, evaluates supported Windows validation, and runs ordered persisted Steps. `DAP.exe --learner-windows` locates the requested top-level application window and composes this runtime from the persisted Guide. Each Guide Step still has one runtime-specific target; hybrid Guides are represented by an ordered mix of Web and Windows Steps rather than by adding parallel Web/Windows targets to one Step.
