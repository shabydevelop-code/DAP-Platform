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
