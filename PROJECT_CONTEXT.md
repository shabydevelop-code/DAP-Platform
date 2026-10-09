# Project Context

DAP Platform is a Digital Adoption Platform for guided learning across Web and Windows applications.

## Documentation current-state rule

Project Markdown files describe the current valid state only. They are not historical journals.

When architecture, requirements, decisions, runner modes, implementation status, or other project rules change, obsolete documentation must be replaced or removed rather than retained beside the new state.

Retired implementations, superseded decisions, old verification baselines, old Step counts, obsolete commands, and migration narratives must not remain in current project Markdown merely for historical reference.

Git history is the historical record. Current Markdown is the current project contract.

This rule applies to all project-level Markdown, including `PROJECT_CONTEXT.md`, `REQUIREMENTS.md`, `ARCHITECTURE.md`, `CURRENT_STATUS.md`, and `DECISIONS.md`.

## Repository write retry rule

When a GitHub repository write fails or is blocked, retry the operation after re-reading the current file and SHA, checking whether the intended change was already applied. Do not immediately report that repository editing is unavailable after one failure. Use bounded retries; never overwrite concurrent changes or claim success without verifying the saved content on the current `main` branch. If retries still fail, report the unresolved failure explicitly.

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
demos\Shared\data\testcrm.db
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

## Launch contract

`DAP.exe --guide <GuideId>` starts the current Learner directly. There is no `--learner` flag or compatibility alias. Instructor/Editor launch is not yet designed. Avoid retaining superseded launch modes or fallback paths solely for compatibility.

## Runner contract

Product Learner launch contract:

```text
DAP.exe --guide <GuideId>
```

TestCRM runner execution contract:

```text
--manual --guide <GuideId>
--hybrid --guide <GuideId>
```

The runner does not select the Guide. It passes the explicitly supplied persisted Guide ID to `DAP.exe --guide <GuideId>`.

Maintenance/path options:

```text
--reset-guide
--published-dap
```

## Independent Web execution

`demos/Web/Launcher` runs the TestCRM backend and Web host without launching DAP. The standalone TestCRM Web host opens Chrome at `http://localhost:5200` after the application becomes ready. The Learner is then started independently with the product launch contract above. Web Application Context `crm` resolves the existing browser application. The verified full Manual run ended the DAP process without closing Chrome or TestCRM.

The Web and Windows E2E runners remain separate regression tools; they are not required for standalone production Learner launch.

## Independent Windows execution

`demos/Windows/Launcher` starts the TestCRM backend and Windows application without launching DAP. The separately launched Learner attaches to the already-open window using persisted context `crm-windows`. User testing reported successful standalone Guide execution with the target remaining open and Learner process termination appearing correct. Windows multi-context switching, delayed discovery, and rebinding are not implemented.

## Ownership rule

Action drivers perform learner actions only. Persisted Guide data plus production Runtime own target resolution, validation, completion, capture required by the Guide, and Step advancement.

Hybrid must not introduce hidden detection or a second completion engine.

## Timeout rule

Automated technical waits must not be increased beyond five seconds without explicit approval. Human response time in Manual/Hybrid is not governed by this automated timeout.

## Next product phase

Instructor/Editor is the next major product phase. It must author the same persisted model used by Learner. AI may be optional assistance but cannot be required for production authoring or execution.

## Product execution modes

Production CLI: `DAP.exe --guide <GuideId> --mode manual|hybrid`. Manual is default. Windows Hybrid is implemented in the production learner, not the TestCRM test driver. It resolves the UIA target and applies persisted AutomationValue to writable controls. Edit controls require focus, value confirmation and TAB blur; runtime validation determines advancement. Other Steps are manual. Existing SQLite columns support this without a schema change. Web and Windows production Hybrid are implemented; the user reported successful full 55-step Hybrid runs on both platforms before the latest Web runtime rename. Manual and post-rename end-to-end regression remain unverified.

## Shared guide execution — current implementation

- `DAP.Core.Guides.GuideExecutionEngine` owns Guide ordering, capture-token materialization, step-shape preflight, disabled-step handling, centered-information dispatch, and lifecycle diagnostics through `IGuideStepAdapter`.
- `UnifiedGuideStepEngine` runs the active-step observation loop for Web and Windows, delegates context/readiness and completion decisions to shared `GuideActiveStepState`, and invokes platform-specific reconciliation callbacks. `GuideActiveStepState` also owns hybrid-value application state and shared wait transitions; platform-specific event observation, target resolution, and bubble effects remain in the adapters.
- `GuideStepExecutionPolicy`, `GuideValidationPolicy`, and `GuideCompletionPolicy` contain runtime-independent classification and validation rules. Web DOM events and Windows UI Automation observations remain platform-specific.
- Web `WebGuideStepRuntime` and Windows `WindowsGuideRuntime` retain platform-specific reconciliation callbacks and UI effects. Their presence alone does not establish duplicated learner policy; shared decisions reside in Core, and remaining decisions require responsibility-by-responsibility review before further extraction or deletion.
- The user confirmed a successful local `DAP.App` build after the Web step-runtime rename and application-host reference correction. Manual end-to-end regression after these changes remains unverified.
- Web startup browser activation parity with Windows remains an open item. The user reported successful full 55-step Web and Windows Hybrid runs before the latest Web runtime rename; post-rename end-to-end regression and Manual runs are not confirmed.
