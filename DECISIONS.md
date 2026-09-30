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
