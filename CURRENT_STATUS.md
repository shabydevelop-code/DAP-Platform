# Current Status

## Current verified baseline — 2026-10-06

### Web

The canonical persisted Web Guide contains **55 Steps**. Step 55 is the persisted centered completion/summary Step; completion is Guide data rather than a special post-Guide bubble.

Verified product baseline:

- Full runner-free Manual Web learner execution: **PASS**.
- Full DB-driven Hybrid Web learner execution: **PASS** before the latest presentation cleanup; the latest Runtime-owned automatic-label presentation is visually verified.
- Web uses the installed DAP Extension + Native Host + .NET Runtime. Playwright is not part of the active Web path.
- Persisted Guide data plus production Runtime own target resolution, validation, completion, capture, disabled-Step skipping, and progression.
- Text entry commits through real edit followed by blur; Hybrid uses Tab only to reproduce that natural commit.
- Stable resolved-Step reconciliation is 500 ms; recovery remains 100 ms. Manual idle measurement was approximately 2% CPU at the accepted stable cadence.
- The 5-second technical synchronization ceiling remains unchanged. Hybrid manual-action Steps do not impose a 5-second human-response deadline.

Current Web production path:

```text
DAP.exe / AdapterWebGuideRuntime
    ↕
ExtensionWebBrowserAdapter
    ↕ Named Pipe
DAP.Runtime.Web.NativeHost
    ↕ Native Messaging
Browser Extension
    ↕
content-runtime.js
    ↕
Browser DOM
```

### Hybrid Web model

Hybrid mode is deliberately semi-automatic.

Persisted Step metadata:

- `IsEnabled` — whether the Runtime executes the Step at all.
- `AutomationValue` — optional value that the Hybrid action layer may enter into a value control.

Rules:

- `IsEnabled = false` means complete Runtime skip: no target resolution, bubble, validation, completion wait, or capture.
- Disabled Steps retain their persisted `StepOrder`; enabled Steps are never renumbered.
- `AutomationValue` does not replace validation. Validation defines what the Runtime expects; `AutomationValue` defines what Hybrid may enter.
- Text inputs and text areas are filled automatically from persisted `AutomationValue`.
- Select controls are selected automatically from persisted `AutomationValue`.
- Meaningful business actions remain manual: Search, Save, Delete, delete confirmation, opening records, navigation, and equivalent actions.
- Runtime remains the only Guide engine and decides every transition.

Current disabled repetitive Web Steps:

```text
24, 25, 30, 31, 42, 43, 44, 45
```

Step 29 remains enabled as the meaningful Lead status change that verifies the service field behavior.

### Automatic-Step presentation

In Hybrid presentation, a Step with a non-empty `AutomationValue` is labeled with the localized product text:

```text
אוטומטי
Automatic
```

The label is now part of normal Runtime bubble presentation. It is created together with the bubble from the already-loaded persisted Step metadata.

The obsolete E2E-side badge injection/synchronization path was removed. There is no post-render target matching, DOM badge injection, or badge-specific test-driver operation.

Current extension version for this presentation change:

```text
0.2.10
```

After extension source changes, the installed unpacked extension must be reloaded so Chrome runs the updated content script.

### Canonical Web runner modes

Maintained Web modes are only:

```text
--manual
--hybrid
```

Maintenance operation:

```text
--reset-guide
```

Infrastructure option:

```text
--published-dap <dir>
```

Removed obsolete modes include Guided Fast, Guided Visual, Fast-from-Step, Visual-from-Step, Unguided, and Manual-from-Step. The old Web `DAP_E2E_MODE` and `DAP_E2E_BROWSER` environment-variable model is obsolete.

### Runner boundary

The Runner is test infrastructure, not a product component.

Mental model:

**Runner replaces only the learner's hands. Runtime remains the only brain of the Guide.**

The Runner may perform user actions and observe the active Step to know what action to perform. It must not decide validation/completion or add semantic facts that belong in the persisted Guide.

Hybrid automation is intentionally limited to injecting persisted value data. It does not add target selectors, validation rules, completion conditions, or transition logic.

### Persistent Guide rule

Normal execution uses the persistent Guide database, normally:

```text
C:\ProgramData\DAP\Data\DAP.db
```

Rule:

**Seed initializes. DB owns. Runtime consumes.**

`--reset-guide` explicitly replaces the persisted TestCRM Guide definition from the development seed. This is temporary authoring/test infrastructure until Instructor/Editor owns Guide creation and editing.

### Process ownership and cleanup

The Web runner preflights required ports and never kills an arbitrary unknown port owner.

Processes started by the Runner are Runner-owned and must be cleaned on every exit path, including early browser/extension handshake failure. Browser launch now sits inside the same cleanup scope as the Web host and backend so stale-extension failure cannot intentionally leave those owned children orphaned.

### Closed-target rule

DAP must support closed third-party target applications. Runtime behavior relies only on production-observable interfaces. TestCRM source may be inspected for diagnosis, but source knowledge may not become a runtime target, validation, or completion oracle.

### Timeout rule

Technical E2E synchronization remains capped at 5 seconds unless explicit approval is given to increase it.

This limit is not a human-response deadline. Hybrid waits naturally on manual Steps until the learner performs the required action, the Runtime advances, the target closes, or a real failure occurs.

## Windows

The previously verified Windows baseline remains the full production learner workflow using UI Automation and WPF presentation.

Windows cleanup/refactoring is intentionally separate from the completed Web runner simplification. Do not assume removed Web modes have automatically been removed from Windows.

## Next work

Keep the Web product path stable. Use Manual as the strongest product acceptance reference and Hybrid as the practical semi-automatic regression workflow. Continue Windows work without reintroducing a second Guide engine or Runner-driven product semantics.
