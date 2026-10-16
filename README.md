# GuideMe Platform

Digital Adoption Platform for guided learning across Web and Windows applications.

## Product scope

- One .NET 8 Windows product with Learner and future Instructor/Editor capabilities.
- Web learner runtime through the GuideMe browser Extension and Native Messaging.
- Windows learner runtime through Microsoft UI Automation.
- Web-only, Windows-only, and mixed Web–Windows–Web Guides are supported. Cross-runtime execution has been validated in the user's existing GuideMe workflows. Manual/Hybrid execution modes are optional.
- Persisted Guide data as the learner execution source of truth.
- Hebrew/English UI with RTL/LTR support.
- Provider-independent data architecture with SQLite as the current provider.

## Documentation and source of truth

The current source code defines implemented capabilities and runtime behavior. The active GuideMe database defines persisted Guide configuration. Successful actual executions establish what has been verified. Documentation is supporting context only; never infer support or lack of support solely from a Markdown file.

- [`REQUIREMENTS.md`](docs/REQUIREMENTS.md) — desired behavior and product requirements, not proof of implementation.
- [`DECISIONS.md`](docs/DECISIONS.md) — architectural decisions and design constraints.

Build and run the current source rather than relying on a hand-maintained status snapshot. For runtime verification, distinguish observed test results from expectations or documentation. Component-local documentation may remain beside the component.

Other documentation:
- [Instructor design specification](docs/INSTRUCTOR_DECISION_TREE_SPEC.md)
- [Reference guide from scratch](docs/REFERENCE_GUIDE_FROM_SCRATCH.md)
- [Application context architecture](docs/APPLICATION_CONTEXT_ARCHITECTURE.md)
- [Verified run commands](docs/RUN_COMMANDS.md)
