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


## Repository identity and test isolation

- The canonical Source of Truth and primary repository for this project is `shabydevelop-code/DAP-Platform` (`DAP-Platform`). `Generic-Web-Training-Platform` may be inspected as a reference for prior ideas, implementations, and proven behaviors, but it is not the primary DAP repository, its architecture must not be assumed to apply to DAP, and DAP development changes must be made in `DAP-Platform` unless explicitly requested otherwise.
- Bubble behavior, target resolution, and their shared domain/runtime contracts are production product code and must remain isolated from the test projects.
- Tests may consume public production contracts and verify behavior, but must not contain the production implementation of Bubble, TargetDescriptor, Locator, Anchor, FrameContext, Validation, or runtime resolution logic.
- The Bubble/target model must support both Web and Windows through shared runtime-neutral Core contracts, with runtime-specific adapters behind those contracts.
- Learner Runtime and Instructor Runtime are separate consumers/modes. Build the Learner Runtime first; Instructor/Editor support follows later.
- SQLite is only the current persistence provider. Core models, including TargetDescriptor, must remain persistence-independent.
- A target may require multiple anchors for unique identification. Ambiguous target resolution must return an explicit ambiguous result and must never guess or silently select the first candidate.
- The current ten-scenario E2E baseline must remain passing while production bubble/runtime capabilities are introduced.

## Current E2E validation baseline

The permanent DAP.TestCRM target is used to validate the Web Runtime against a PeopleSoft-style server-backed CRM model. The current validated E2E flow includes:

1. Case FieldChange followed by Content iframe replacement.
2. Server validation failure with preservation of unsaved working values.
3. Server-side Grid rerender/reorder with target re-resolution.
4. Content-document reload with preservation of the logical Case context and saved values.
5. CRM tab switching between Cases and Leads while preserving the business context.
6. Conditional business target disappearance/reappearance with target re-resolution.
7. Cross-frame navigation from the Header frame to the Content frame through a real user action.
8. Layout Shift caused by an existing dependent business field, followed by target re-resolution.
9. Consecutive server updates with final-state target re-resolution.
10. Business-context switching with target isolation.

The same run also covers Lead creation, Lead FieldChange/conditional validation, dynamic Lead deletion, Case deletion, and the complete Customer -> Site -> Case -> Lead workflow. The E2E runner has fast and visual modes; fast is the default validation mode.

These scenarios must exercise user-visible application behavior and generic runtime mechanisms. TestCRM-specific workarounds must not be introduced merely to make an E2E scenario pass when the corresponding behavior would not exist in an independent target CRM.


### Validation coverage
The validated E2E baseline already includes a real CRM value-validation flow in the Case workflow: changing status to `סגורה` makes `closeReason` required; an attempted save without that value is rejected by server validation while unsaved Subject and Description values are preserved; supplying `closeReason` (`טופל`) then allows the save to succeed. This validation currently runs without DAP bubbles and is part of the underlying CRM/E2E behavior, not a bubble-specific test.


### Target resolution model
A target is represented by a runtime-neutral TargetDescriptor rather than a single selector. It identifies the target through a primary locator plus zero or more anchors/context constraints. Resolution must discover candidates, apply the anchors, verify uniqueness, and return an explicit ambiguous/not-found result rather than guessing. The descriptor also carries the runtime and frame context required by the corresponding Web or Windows adapter. This model is intended to support re-resolution after DOM changes, iframe replacement, grid rerender/reorder, layout shifts, target disappearance/reappearance, and equivalent Windows UI changes.
