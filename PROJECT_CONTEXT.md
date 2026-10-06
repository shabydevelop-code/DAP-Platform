# Project Context

DAP Platform is a Digital Adoption Platform for guided learning across Web and Windows applications.

## Documentation current-state rule

Project Markdown files describe the current valid state only. They are not historical journals.

When architecture, requirements, decisions, runner modes, implementation status, or other project rules change, obsolete documentation must be replaced or removed rather than retained beside the new state.

Retired implementations, superseded decisions, old verification baselines, old Step counts, obsolete commands, and migration narratives must not remain in current project Markdown merely for historical reference.

Git history is the historical record. Current Markdown is the current project contract.

This rule applies to all project-level Markdown, including `PROJECT_CONTEXT.md`, `REQUIREMENTS.md`, `ARCHITECTURE.md`, `CURRENT_STATUS.md`, and `DECISIONS.md`.

## Source of truth

The current `main` branch in GitHub is the authoritative code source. Historical commits, old branches, prior snapshots, and conversation excerpts do not define current implementation state unless historical investigation is explicitly requested.

Persisted Guide data is the learner execution source of truth.

## Product architecture

- `DAP.exe`: .NET 8 / WPF application.
- `DAP.Core`: runtime-neutral Guide/domain contracts.
- `DAP.Data`: persistence abstractions.
- `DAP.Data.Sqlite`: current SQLite provider.
- `DAP.Runtime.Web`: .NET Web learner runtime.
- `DAP.Runtime.Web.NativeHost`: Native Messaging bridge.
- `DAP.Runtime.Web.Extension`: browser Extension adapter.
- `DAP.Runtime.Windows`: Windows learner runtime using UI Automation.

Production Web does not use Playwright. Production Windows uses UI Automation.

## Production boundary

DAP must support closed third-party applications. Product Runtime must not depend on target source code, internal target databases, private APIs, TestCRM-specific knowledge, Playwright, or AI.

TestCRM source may be inspected only for development diagnosis.

## Databases

DAP default database:

```text
C:\ProgramData\DAP\Data\DAP.db
```

TestCRM business database:

```text
test-apps\DAP.TestCRM\data\testcrm.db
```

These databases have different ownership and must remain separate.

## Current canonical Guides

Web:

```text
testcrm-web-canonical-workflow
```

Windows:

```text
testcrm-windows-canonical-workflow
```

Both repository seeds define 55 persisted Steps. Step 55 is the persisted centered summary.

## Runner contract

Supported learner modes:

```text
--manual
--hybrid
```

Maintenance/path options:

```text
--reset-guide
--published-dap
```

Old Guided/Unguided/Fast/Visual/From-Step modes and `DAP_E2E_MODE` are retired.

## Ownership rule

Action drivers perform learner actions only. Persisted Guide data plus production Runtime own target resolution, validation, completion, capture required by the Guide, and Step advancement.

Hybrid must not introduce hidden detection or a second completion engine.

## Timeout rule

Automated technical waits must not be increased beyond five seconds without explicit approval. Human response time in Manual/Hybrid is not governed by this automated timeout.

## Next product phase

Instructor/Editor is the next major product phase. It must author the same persisted model used by Learner. AI may be optional assistance but cannot be required for production authoring or execution.
