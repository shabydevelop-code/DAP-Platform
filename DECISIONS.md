# Architectural Decisions

## ADR-001 — One desktop application

**Status:** Accepted

DAP is one Windows desktop application containing Learner and Editor modes rather than separate executables.

## ADR-002 — Desktop technology

**Status:** Accepted

Use .NET 8 and WPF for the production desktop application.

## ADR-003 — Web Runtime

**Status:** Accepted

Use Microsoft Playwright for .NET as the production Web Runtime technology. DAP.exe integrates with the .NET runtime directly; Python is not a product dependency. Playwright remains behind DAP runtime contracts rather than becoming a dependency of the Core domain.

## ADR-004 — Windows Runtime

**Status:** Accepted

Use Microsoft UI Automation (UIA) for native Windows target discovery and interaction observation.

## ADR-005 — Shared guide model

**Status:** Accepted

Web-only, Windows-only, and hybrid guides use the same Guide/Step/Validation/Progress domain model. Each step declares its runtime.

## ADR-006 — Database independence

**Status:** Accepted

Core/domain logic must not depend on a database engine. SQLite is the first provider; additional providers may be introduced later.

## ADR-007 — Localization

**Status:** Accepted

The GUI supports Hebrew and English by user choice, including RTL/LTR. GUI language is independent from guide-content language.

## ADR-008 — .NET deployment prerequisite

**Status:** Accepted

Target machines are assumed to have the .NET 8 Desktop Runtime installed. Builds are framework-dependent. Missing-runtime detection must produce a clear user-facing failure.

## ADR-009 — Markdown as project state

**Status:** Accepted

Persistent project context, architecture, decisions, requirements, and current progress are maintained as Markdown files in the repository and updated alongside significant implementation changes.

## ADR-010 — Production-first architecture

**Status:** Accepted

DAP-Platform is developed as the production product. POCs may be used as historical evidence or isolated experiments, but production code and architecture must not depend on POC packaging, Python scripts, temporary test harnesses, or legacy GWTP implementation details.

## ADR-011 — Guide bubble navigation is context-aware

**Status:** Accepted

Step order is not treated as application navigation.

Action Steps advance automatically only after their validation succeeds. Informational Steps may expose a manual Next action.

Previous is not a universal navigation control. It may be exposed only when the active runtime can determine that the previous Step is safely renderable in the current application context. DAP must not assume that `StepOrder - 1` can be displayed after page navigation, context replacement, application changes, or Web/Windows runtime transitions.

The shared Step model therefore includes advance behavior and sufficient context/navigation metadata for runtime-aware navigation decisions.


## ADR-012 — Server-backed Web refresh preserves logical context

**Status:** Accepted

In server-backed Web applications, including PeopleSoft-style applications, a server round trip may refresh or rebuild the DOM without changing the user's logical business context.

DAP must treat DOM references as transient across server calls. DOM replacement alone does not mean that the application context changed and does not mean that a Step completed.

After a server-triggered refresh, the Web Runtime must re-evaluate the current logical context, re-resolve the active Step target, and continue the same Step when the business context is still valid. Step advancement remains governed by validation success.

This behavior is a general Web Runtime rule for server-backed application mode and must not be implemented as application-specific logic for TestCRM or PeopleSoft.


## ADR-013 — Web target context includes iframe hierarchy

**Status:** Accepted

Server-backed Web applications may split application chrome and active business content across separate iframes. The Web Runtime must treat iframe/frame hierarchy as part of target context rather than assuming all targets belong to the top-level document.

DAP must be able to resolve the appropriate frame and then the target within that frame. If a server action reloads or replaces a frame, both the frame and target references are considered transient and must be re-resolved while preserving the logical business context when applicable.

This is a general Web Runtime requirement and is validated by DAP.TestCRM using separate header and content frames.


## ADR-014 — Web context includes transient working state

**Status:** Accepted

For application-triggered server round trips, preserving Web context includes relevant unsaved working values in addition to the logical record, screen, tab, and navigation state.

A server refresh may rebuild the iframe/document and restore values that have not yet been persisted to the database. DAP must treat the restored post-refresh UI as the current state and must not equate persistence with context preservation.

DAP.TestCRM preserves transient form values across its simulated PeopleSoft-style server refreshes so this behavior can be validated independently from database saves. A user-initiated browser reload is not required to preserve unsaved working state.


## ADR-015 — DAP.TestCRM permanently follows a PeopleSoft-style server interaction model

**Status:** Accepted

DAP.TestCRM is a permanent production-runtime test application and must consistently model PeopleSoft-style server-backed behavior.

Any action that logically requires the server must be implemented as a server round trip with content refresh/reconstruction rather than as a purely client-side SPA mutation. The application must preserve logical business context and relevant transient unsaved working state across that refresh unless the action intentionally navigates to a different context.

This applies to search, grid sorting, saves, updates, validation failures, and future server-backed interactions.

Server-side validation remains authoritative. Validation errors and their message text originate on the server; after the server-style refresh restores the user's working context, the client presents the returned error in the PeopleSoft-style modal.

Future TestCRM changes must preserve this contract unless this ADR is explicitly superseded.


## ADR-016 — Server round trips keep the current business screen visible

**Status:** Accepted

DAP.TestCRM must provide consistent feedback for server-backed operations without replacing the active business screen with a generic loading state.

While a server request is in progress, the existing content remains visible whenever possible and a compact activity indicator displays a spinner with "מעבד...". Interaction may be temporarily blocked to prevent duplicate operations. The Content iframe must not display an intermediate "טוען..." placeholder during reconstruction.

After the server round trip and context restoration complete, the application presents the operation result in the restored context. Successful saves may use a transient success message; validation and server errors continue to use the authoritative server message in the standard modal.

This rule applies system-wide to TestCRM server-backed actions, including search, sorting, FieldChange, save/update, delete, and validation flows.


## ADR-017 — Server validation identifies and marks invalid fields

**Status:** Accepted

Server-side validation remains authoritative. Validation responses must identify the fields that failed validation in addition to returning the validation message.

After the PeopleSoft-style server round trip restores the working context, DAP.TestCRM marks each server-rejected field with a red error border and `aria-invalid="true"`, while also presenting the server-returned message in the standard error modal. The client must not infer invalid fields independently from the server rules.

A subsequent successful validation/refresh clears the error state because the rebuilt screen has no server validation result to restore.


## ADR-018 — Separate fast validation from visual E2E demonstration

**Status:** Accepted

The permanent DAP.TestCRM E2E runner supports two execution modes through `DAP_E2E_MODE`.

`fast` is the default validation mode. It skips artificial human-like interaction delays and bypasses TestCRM's artificial server-thinking delay for E2E requests. It does not bypass real application readiness: server responses, route completion, DOM/frame replacement, validation, and other actual synchronization conditions remain required.

`visual` is demonstration mode and retains cursor movement, typing delays, processing feedback, and the artificial server-thinking delay.

The two modes must share the same test logic. Maintaining separate test implementations is not permitted merely to support visual demonstration.

## ADR-019 — Real readiness over fixed synchronization delays

**Status:** Accepted

E2E synchronization must use real application signals wherever possible. Fixed delays such as the TestCRM artificial server-thinking delay or human-like pauses must not be used as a substitute for route, DOM, iframe, server-state, or validation readiness.

The DAP.TestCRM PeopleSoft-Web flow therefore re-resolves the active Content iframe and exposes route readiness after each route transition. The E2E frame polling interval remains 100ms and is considered polling infrastructure, not a demonstration delay.


## ADR-020 — E2E scenarios must validate target-independent runtime behavior

**Status:** Accepted

The permanent DAP.TestCRM E2E suite is a representative server-backed CRM target, not the production CRM itself. E2E scenarios must exercise behavior that a separate real CRM could reasonably expose through its user-visible Web application.

Test-specific workarounds, hidden navigation APIs, route persistence added solely for tests, or application-specific hooks must not be introduced merely to make an E2E scenario pass. When a scenario requires such a workaround, the scenario or the generic DAP Web Runtime contract must be reconsidered.

Validated scenarios currently cover server-driven FieldChange and Content iframe replacement, validation with preserved unsaved working values, Grid rerender/reorder and target re-resolution, Content-document reload with preserved logical context, CRM tab switching, conditional target disappearance/reappearance, and cross-frame Header-to-Content navigation. The existing baseline workflow and deletion coverage must remain regression-protected as new scenarios are added.
