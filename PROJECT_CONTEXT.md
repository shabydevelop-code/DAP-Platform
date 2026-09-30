# Project Context

## Product

DAP Platform is a production-target Digital Adoption Platform for creating and running interactive guides across Web and Windows applications.

The architecture documented in this repository is the product architecture. It must not be described or implemented as a proof of concept.

## Application

The product is a single Windows desktop application with two user modes:

- Learner — discovers, starts, continues, and completes guides.
- Editor — creates, records, edits, previews, and manages guides.

The application must not couple the core guide model to the GUI technology.

## Runtime scope

A guide can be:

- Web only.
- Windows only.
- Hybrid, moving between Web and Windows steps.

Web and Windows runtimes share the same Guide / Step / Validation / Progress model.

## Runtime technologies

- Web: Microsoft Playwright for .NET.
- Windows: Microsoft UI Automation (UIA).
- Desktop GUI: .NET 8 + WPF.

Playwright and UIA are production runtime components behind DAP runtime contracts; they are not temporary POC technologies.

The production application must not require Python. DAP.exe uses the .NET Web Runtime directly.

## Localization

The GUI must support Hebrew and English by user choice.

- Hebrew: RTL.
- English: LTR.
- GUI language and guide-content language are separate concepts.
- GUI strings must be resource-based and must not be hard-coded into views.

## Data

The data layer must be provider-independent.

- SQLite is the default/first database provider.
- Core/domain logic must not depend directly on SQLite.
- Additional database providers must be possible without rewriting guide/runtime logic.

## Target environment

The target Windows machine is assumed to have the .NET 8 Desktop Runtime installed.

Distribution is framework-dependent.

The application must detect a missing required runtime during installation/startup and fail with a clear message.

## Documentation rule

Project progress and architectural configuration are maintained in Markdown in this repository.

After significant implementation changes, update the relevant Markdown documentation in the same change.
