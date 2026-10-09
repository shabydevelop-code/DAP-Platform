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

`tests/DAP.TestCRM.Web.Host` runs the TestCRM backend and Web host without launching DAP. The standalone TestCRM Web host opens Chrome at `http://localhost:5200` after the application becomes ready. The Learner is then started independently with the product launch contract above. Web Application Context `crm` resolves the existing browser application. The verified full Manual run ended the DAP process without closing Chrome or TestCRM.

The Web and Windows E2E runners remain separate regression tools; they are not required for standalone production Learner launch.

## Independent Windows execution

`tests/DAP.TestCRM.Windows.Host` starts the TestCRM backend and Windows application without launching DAP. The separately launched Learner attaches to the already-open window using persisted context `crm-windows`. User testing reported successful standalone Guide execution with the target remaining open and Learner process termination appearing correct. Windows multi-context switching, delayed discovery, and rebinding are not implemented.

## Ownership rule

Action drivers perform learner actions only. Persisted Guide data plus production Runtime own target resolution, validation, completion, capture required by the Guide, and Step advancement.

Hybrid must not introduce hidden detection or a second completion engine.

## Timeout rule

Automated technical waits must not be increased beyond five seconds without explicit approval. Human response time in Manual/Hybrid is not governed by this automated timeout.

## Next product phase

Instructor/Editor is the next major product phase. It must author the same persisted model used by Learner. AI may be optional assistance but cannot be required for production authoring or execution.

## Product execution modes

Production CLI: `DAP.exe --guide <GuideId> --mode manual|hybrid`. Manual is default. Windows Hybrid is implemented in the production learner, not the TestCRM test driver. It resolves the UIA target and applies persisted AutomationValue to writable controls. Edit controls require focus, value confirmation and TAB blur; runtime validation determines advancement. Other Steps are manual. Existing SQLite columns support this without a schema change. The user reported Windows Hybrid appeared correct; full regression is not confirmed. Web production Hybrid now has an extension-backed persisted-value executor; regression is pending.

## Shared guide execution policy

`DAP.Core.Guides.GuideRunPlan` is the common runtime-neutral owner of ordered Guide Steps, start-step selection, disabled-step lookahead, captured values, runtime capture-token substitution, and asynchronous step lifecycle orchestration. Both the Web adapter guide runtime and Windows guide runtime use it. Target resolution, active-step validation, bubble presentation, and UI-specific actions remain in their respective runtimes/adapters; a single unified active-step validation/presentation engine has **not** yet been implemented. Session termination and build-lock preflight are handled by the shared DAP.App host. Web tab/window activation remains an open gap; production Web Hybrid value automation is implemented but unverified. This is current architecture, not a completed full runtime unification.

## Shared value-validation policy

`DAP.Core.Guides.GuideValidationPolicy` now evaluates persisted `value-equals` and `value-not-empty` rules using observed values supplied by runtime adapters. `WindowsValidationEvaluator` delegates these checks to Core while retaining Windows UI Automation `ValuePattern` access. The Web browser adapter also delegates value checks to Core, preserving its existing missing-expected-value behavior. Web event/commit handling, completion conditions, target resolution, and bubble presentation are unchanged. This is a focused first step, not a completed unified active-step validation engine. A fresh build and Web/Windows regression are still required.

### Shared completion-condition policy

`DAP.Core.Guides.GuideCompletionPolicy` evaluates `target-exists`, `target-not-exists`, `target-enabled`, and `value-equals` from runtime observations. Both Web and Windows now delegate these four checks to Core. Target inspection/resolution remains adapter-specific; Windows `target-replaced` still compares UIA element identities locally. Web retains its previous treatment of unresolved targets for `target-not-exists`. Build and regression verification of this change are pending.

### Production Web Hybrid implementation (pending regression)

The production Web learner now accepts `DAP.exe --guide <GuideId> --mode hybrid`. After target resolution and presentation readiness, it applies persisted `AutomationValue` once through the extension to a uniquely resolved writable text input/textarea, using focus, native value setter, input/change events and blur. The normal validation/commit and completion-condition pipeline still controls advancement. Steps without `AutomationValue` remain user-operated. The extension command is production-scoped and does not invoke TestCRM test-driver operations. No claim of end-to-end PASS is made until a local build and full Web/Windows Manual/Hybrid regressions are reported. Non-text value automation and browser tab activation are not implemented.

Standalone TestCRM Web Host opens the application in Chrome automatically after the five-second readiness check. DAP.exe remains a separate product process and does not own the target application's browser or servers. This host change requires a local Windows execution check.

The standalone TestCRM Web Host treats user-requested Ctrl+C termination as normal shutdown, stops its owned Web and backend processes, and does not report their exit code 0 as an unexpected failure. Unexpected independent process exits remain errors. Requires local verification.

Guide execution consolidation status (in progress): both Web and Windows guide runners now invoke DAP.Core.Guides.GuideExecutionEngine for shared sequence lifecycle, capture-state ownership, and diagnostics. Platform-specific active-step loops remain in their respective runtimes and must be refactored before the single-engine architecture can be considered complete. Build and regression tests have not yet been verified on Windows.

Core GuideStepExecutionPolicy now enforces centered information-step invariants for both Web and Windows, and Web hybrid value-step eligibility. Active-step loops remain platform-specific. Build and E2E regression are pending; do not mark engine consolidation complete.

Both Web and Windows Hybrid value entry now apply the same Core GuideStepExecutionPolicy eligibility check. Platform-specific value assignment and active-step execution remain in the adapters. Full engine unification is not yet complete; build and regression verification pending.

GuideStepExecutionPolicy now classifies centered-information versus target-attached steps consistently for Web and Windows, rejecting invalid target/presentation combinations. Web automatic-validation eligibility also delegates to Core. Active-step execution loops are still separate; Windows build and end-to-end regressions remain to be verified.

Current consolidation: Core GuideStepExecutionPolicy owns automatic validation, click and target-disappearance classification, plus the committed-text validation gate and primary/completion advancement decision. Windows uses both new gates; Web uses the shared advancement decision. Platform-specific active-step loops remain; full engine unification and runtime regressions are pending.

Current architecture: GuideExecutionEngine dispatches through IGuideStepAdapter; Web and Windows provide DelegateGuideStepAdapter implementations. The engine owns step ordering, capture-token materialization, step-shape preflight and lifecycle diagnostics. Core now also owns the observed-action completion decision used for Web clicks and Windows click/target disappearance. Active-step reconciliation loops remain platform-specific; full consolidation and E2E regression tests are outstanding.
