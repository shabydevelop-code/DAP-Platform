# Architectural Decisions

## ADR-001 — One desktop application

**Status:** Accepted

DAP is one Windows desktop application containing Learner and Editor modes rather than separate executables.

## ADR-002 — Initial desktop technology

**Status:** Accepted

Use .NET 8 and WPF for the initial desktop application.

## ADR-003 — Web Runtime

**Status:** Accepted for initial implementation

Use Playwright as the Web Runtime technology. Playwright is an adapter behind DAP runtime contracts, not a dependency of the Core domain.

## ADR-004 — Windows Runtime

**Status:** Accepted for initial implementation

Use Microsoft UI Automation (UIA) for native Windows target discovery and interaction observation.

## ADR-005 — Shared guide model

**Status:** Accepted

Web-only, Windows-only, and hybrid guides use the same Guide/Step/Validation/Progress domain model. Each step declares its runtime.

## ADR-006 — Database independence

**Status:** Accepted

Core/domain logic must not depend on a database engine. SQLite is the initial provider; additional providers may be introduced later.

## ADR-007 — Localization

**Status:** Accepted

The GUI supports Hebrew and English by user choice, including RTL/LTR. GUI language is independent from guide-content language.

## ADR-008 — .NET deployment prerequisite

**Status:** Accepted

Target machines are assumed to have the .NET 8 Desktop Runtime installed. Initial builds are framework-dependent. Missing-runtime detection must produce a clear user-facing failure.

## ADR-009 — Markdown as project state

**Status:** Accepted

Persistent project context, architecture, decisions, requirements, and current progress are maintained as Markdown files in the repository and updated alongside significant implementation changes.
