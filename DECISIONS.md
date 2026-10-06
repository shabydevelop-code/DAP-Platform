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


## ADR-015 — DAP.TestCRM permanently follows a PeopleSoft-style server interaction model

**Status:** Accepted

DAP.TestCRM is a permanent production-runtime test application and must consistently model PeopleSoft-style server-backed behavior.

Any action that logically requires the server must be implemented as a server round trip with content refresh/reconstruction rather than as a purely client-side SPA mutation. The application must preserve logical business context and relevant transient unsaved working state across that refresh unless the action intentionally navigates to a different context.

This applies to search, grid sorting, saves, updates, validation failures, and future server-backed interactions.

Server-side validation remains authoritative. Validation errors and their message text originate on the server; after the server-style refresh restores the user's working context, the client presents the returned error in the PeopleSoft-style modal.

Future TestCRM changes must preserve this contract unless this ADR is explicitly superseded.


## ADR-016 — Server round trips keep the current business screen visible

**Status:** Accepted

DAP.TestCRM must provide consistent feedback for server-backed operations without replacing the active business screen with a generic loading state.

While a server request is in progress, the existing content remains visible whenever possible and a compact activity indicator displays a spinner with "מעבד...". Interaction may be temporarily blocked to prevent duplicate operations. The Content iframe must not display an intermediate "טוען..." placeholder during reconstruction.

After the server round trip and context restoration complete, the application presents the operation result in the restored context. Successful saves may use a transient success message; validation and server errors continue to use the authoritative server message in the standard modal.

This rule applies system-wide to TestCRM server-backed actions, including search, sorting, FieldChange, save/update, delete, and validation flows.


## ADR-017 — Server validation identifies and marks invalid fields

**Status:** Accepted

Server-side validation remains authoritative. Validation responses must identify the fields that failed validation in addition to returning the validation message.

After the PeopleSoft-style server round trip restores the working context, DAP.TestCRM marks each server-rejected field with a red error border and `aria-invalid="true"`, while also presenting the server-returned message in the standard error modal. The client must not infer invalid fields independently from the server rules.

A subsequent successful validation/refresh clears the error state because the rebuilt screen has no server validation result to restore.


## ADR-018 — Separate fast validation from visual E2E demonstration

**Status:** Accepted

The permanent DAP.TestCRM E2E runner supports two execution modes through `DAP_E2E_MODE`.

`fast` is the default validation mode. It skips artificial human-like interaction delays and bypasses TestCRM's artificial server-thinking delay for E2E requests. It does not bypass real application readiness: server responses, route completion, DOM/frame replacement, validation, and other actual synchronization conditions remain required.

`visual` is the observable presentation mode. It retains visible cursor movement and presentation pacing/feedback around the same learner actions used by Fast. Mode-dependent typing or commit semantics are not permitted. Artificial TestCRM thinking delay may remain fixture presentation behavior, but it must not replace real readiness.

The two modes must share the same test logic. Maintaining separate test implementations is not permitted merely to support visual demonstration.

## ADR-019 — Real readiness over fixed synchronization delays

**Status:** Accepted

E2E synchronization must use real application signals wherever possible. Fixed delays such as the TestCRM artificial server-thinking delay or human-like pauses must not be used as a substitute for route, DOM, iframe, server-state, or validation readiness.

The DAP.TestCRM PeopleSoft-Web flow therefore re-resolves the active Content iframe and exposes route readiness after each route transition. The E2E frame polling interval remains 100ms and is considered polling infrastructure, not a demonstration delay.


## ADR-020 — E2E scenarios must validate target-independent runtime behavior

**Status:** Accepted

The permanent DAP.TestCRM E2E suite is a representative server-backed CRM target, not the production CRM itself. E2E scenarios must exercise behavior that a separate real CRM could reasonably expose through its user-visible Web application.

Test-specific workarounds, hidden navigation APIs, route persistence added solely for tests, or application-specific hooks must not be introduced merely to make an E2E scenario pass. When a scenario requires such a workaround, the scenario or the generic DAP Web Runtime contract must be reconsidered.

Validated scenarios currently cover server-driven FieldChange and Content iframe replacement, validation with preserved unsaved working values, Grid rerender/reorder and target re-resolution, Content-document reload with preserved logical context, CRM tab switching, conditional target disappearance/reappearance, cross-frame Header-to-Content navigation, and Layout Shift with target re-resolution. The existing baseline workflow and deletion coverage must remain regression-protected as new scenarios are added.

## ADR-021 — Persisted Guide database owns initialized Guides

**Status:** Accepted

Guide seed/factory definitions initialize or explicitly reset known Guides; they are not authoritative during normal execution after initialization.

Normal execution loads and runs the persisted Guide from the configured DAP data provider. Instructor/Editor changes written to persistence therefore become authoritative for subsequent Learner and E2E execution.

Product rule: **Seed initializes. DB owns. Runtime consumes.**

SQLite is the current provider only. This ownership rule belongs to the persistence architecture and must remain independent of the concrete database engine.

## ADR-022 — Learner is a runtime, not a mandatory dashboard

**Status:** Accepted

The current learner product flow is organization-provided launch/shortcut/portal -> specific Guide -> Learner Runtime -> in-application bubbles -> completion.

DAP does not require a persistent Learner dashboard or generic between-Step loading/progress surface. Between Steps the Runtime may remain visually quiet while it re-resolves the next target. An optional catalog/launcher may be introduced only as a separate future product requirement.

## ADR-023 — Web bubble presentation may be promoted independently of target ownership

**Status:** Accepted

A resolved target and its validation remain owned by the original document/frame. If that frame is physically unable to display the learner bubble, presentation may be promoted to the top-level page without changing target identity or validation ownership.

The promoted bubble is an interactive production learner surface, not a passive diagnostic overlay. This rule is generic and must not be implemented as a Step-, site-, or TestCRM-specific workaround.

## ADR-024 — Bubble dragging uses an explicit handle

**Status:** Accepted

Draggable learner bubbles expose a visible `⠿` handle. Only that handle starts a drag; the rest of the bubble must not advertise or initiate dragging.

The interaction contract is: normal content uses the default cursor, handle hover uses `grab`, and active dragging uses `grabbing`. The rule applies consistently to regular Web bubbles, promoted top-level bubbles, and the Guide completion bubble.

## ADR-025 — Guide completion requires explicit learner confirmation

**Status:** Accepted

After the final Step completes, the Web Learner Runtime presents a completion state with an explicit `סיום` action and waits for the learner's real click. DAP must not synthesize that click.

Finishing the Guide/runtime is separate from the lifecycle of the target business browser. The completion action does not imply closing the browser.


## ADR-026 — Numeric persistence IDs with stable textual keys

**Status:** Accepted

SQLite persistence uses numeric internal primary/foreign keys for Guides, GuideSteps, and their relationships. Human-readable identifiers are stored separately as stable textual `Key` values.

Runtime and application boundaries may continue to address a Guide by its stable textual key; database row IDs are an internal persistence concern and must not become user-facing or runtime-routing identifiers.

Legacy databases that used textual primary keys are migrated in place, preserving the previous textual IDs as the new keys and remapping all GuideStep and TargetAnchor relationships to numeric IDs.


## ADR-027 — Web E2E owns its TestCRM server topology

**Status:** Accepted

The normal TestCRM Web E2E runner must be self-contained. It starts the shared TestCRM backend and Web host, waits for application readiness, executes the browser/DAP scenario, and cleans up only the processes it created.

Manual pre-start of TestCRM Server or Web is not part of the normal E2E contract. This keeps Fast, Visual, Unguided (called CRM-only when this ADR was originally recorded), and focused runtime validation reproducible from a single runner command.


## ADR-028 — E2E timeout increases above 5 seconds require explicit approval

**Status:** Accepted

The default maximum wait timeout for DAP/TestCRM E2E synchronization is 5 seconds.

A timeout failure must be treated first as evidence of a possible readiness, lifecycle, target-resolution, navigation, server-state, or synchronization defect. Increasing a timeout must not be used as the normal first response to a failing test, because it can hide the actual defect while only making failures slower.

Before proposing any timeout above 5 seconds, the failing transition and its real readiness condition must be investigated. An increase above 5 seconds is a last-resort change only when there is concrete evidence that the underlying operation can legitimately require more than 5 seconds.

**Any change that raises an E2E timeout above 5 seconds requires the user's explicit approval before implementation.** This applies even to temporary diagnostic changes. Polling intervals and intentionally human-paced Visual-mode delays are separate concerns and do not override this rule.


## ADR-029 — Unguided reuses the canonical persisted Guide sequence (historically CRM-only)

**Status:** Accepted

Web Unguided is the canonical CRM business flow without `DAP.exe` and learner bubbles. It is not a separate TestCRM QA scenario.

Normal Guided Web execution and Unguided both consume the persisted `testcrm-web-canonical-workflow` Guide from the configured DAP data provider and follow the same canonical persisted sequence. The current seed contains 54 Steps; historical 53-Step results remain historical only. Guided mode additionally synchronizes with production DAP Runtime/bubble state; Unguided omits that presentation/runtime synchronization.

Production Guide data must not be polluted with test-only action/value fields merely to make Unguided executable. When a Guide validation is intentionally generic, such as `value-not-empty`, the synthetic value entered by the E2E remains a test-fixture concern.

TestCRM's artificial Web/Windows parity must not drive a production schema that gives one Step parallel Web and Windows targets. A real hybrid Guide remains an ordered sequence in which each Step declares its own runtime.

## ADR-030 — Windows learner bubbles follow the shared drag/pointer interaction contract

**Status:** Accepted

Windows learner bubbles use the same interaction principles as Web learner bubbles where the platform permits: an explicit visible drag handle, a directional pointer toward the current resolved target, and initial placement that attempts not to cover the target.

The Windows implementation remains WPF-native and derives target geometry from production-observable UI Automation bounds. Dragging is presentation-only and does not change target identity, validation ownership, or Guide semantics.

## ADR-031 — Small Windows grids use scoped row filtering before specialized large-grid strategies

**Status:** Accepted

When a Windows target is a row in a small, already-realized UIA grid and the persisted descriptor provides a descendant identity, the Windows Runtime may resolve it by enumerating `DataItem` rows within the declared grid scope and filtering those rows by the descendant anchor.

The result must remain explicit and deterministic: exactly one match resolves, zero matches are NotFound, and multiple matches are Ambiguous. The Runtime must not choose the first row silently.

This strategy is based only on UIA information observable from a closed target application. TestCRM source, internal database state, and private APIs must not be used as runtime targeting oracles. Specialized UIA/visual strategies for larger or virtualized grids may coexist behind the same shared target-resolution contract.

## ADR-032 — Manual Windows bubble placement persists for the active Step

**Status:** Accepted

When a learner drags a Windows learner bubble, that manually selected position remains authoritative for the rest of the current Guide Step. Normal Runtime reconciliation must not move the bubble back to an automatically calculated target-relative position while the same Step remains active.

The directional pointer is hidden immediately when manual dragging begins, not only after the drag ends, matching the Web learner-bubble interaction. It remains hidden for the rest of the active Step. When the active Step changes, manual placement is cleared and the next bubble returns to automatic placement with its pointer visible.

This state is presentation-only and must not alter target resolution, validation, runtime capture, or Guide progression.

## ADR-033 — Guide completion rules belong to persisted Guide data

**Status:** Accepted

Any rule that determines whether a real learner is allowed to advance from one Guide Step to the next is part of the Guide's persisted semantics and must be representable in the Guide model and data provider. It must not exist only inside an E2E driver, scenario harness, TestCRM source, or other test-only orchestration.

The persisted Step definition owns the **what** of completion: target, learner action/validation, required destination/context, and any additional completion conditions needed to prove that the business transition is complete. The production Runtime owns only the **how**: resolving those persisted targets and evaluating those persisted conditions through production-observable interfaces.

An E2E driver may automate the learner's action, but it must not contain hidden business rules that are required for progression and unavailable to the real learner Runtime. If an E2E assertion reveals that the next Step is only safe after a destination screen, field, modal, state, or context exists, that requirement must be promoted into the persisted Guide semantics before relying on it as part of the learner flow.

Runtime implementation details such as polling, retries, UIA event handling, modal-window discovery, resolver strategy, and timing mechanics remain code-level concerns and do not belong in the Guide database unless they are explicitly configurable product semantics.


## ADR-034 — Canonical Web and Windows Guides preserve business-scenario parity

**Status:** Accepted

The canonical TestCRM Web and Windows Guides represent the same business workflow. This ADR was originally accepted when each Guide contained 53 persisted Steps; the current repository seeds contain 54 Steps. Runtime-specific target technology may differ, but Step order, learner intent, business identity, and progression semantics must remain aligned.

Where the intended business object has a stable identity, the Guide must encode that identity rather than rely on incidental row order such as "first row". The canonical workflow currently identifies `מטה תל אביב`, `אבי כהן`, and the Case created during the active run explicitly. Runtime capture may be used to carry identities created earlier in the Guide into later Steps.

A change to one platform's canonical Guide that alters the business scenario must be reviewed against the other platform's Guide. Platform-specific mechanics may differ without requiring artificial schema symmetry.

The canonical execution-mode names are **Guided** and **Unguided**. References to **CRM-only** in older ADRs are historical terminology for what is now called Unguided; they do not define a separate current execution mode.

## ADR-035 — Windows learner presentation follows application window state

**Status:** Accepted

Windows learner bubble/highlight presentation remains bound to the active target application.

- Moving or resizing the target application causes bubble/highlight geometry to be recomputed from current UIA bounds.
- A learner-dragged bubble preserves its relative manual offset for the active Step.
- Minimizing the target application or moving foreground ownership to another application hides learner presentation.
- Restoring or returning foreground ownership causes presentation to be rebuilt from current UIA geometry.
- Owned/modal windows of the target application remain part of the same interactive application context.

This behavior is presentation/runtime infrastructure only and does not change persisted target identity, validation, capture, or Guide progression semantics.

## ADR-036 — Value validation completes on natural interaction commit

**Status:** Accepted

A value becoming temporarily valid is not sufficient by itself to complete an automatic learner Step. The Runtime must distinguish the learner's interaction-completion signal from the persisted validation condition.

For editable text controls, completion is evaluated only after a real edit has occurred and the edit is committed by leaving the control. Web implements this through blur after change; Windows implements the equivalent behavior through UIA-observed focus/value state. Discrete controls such as selections commit on their natural change/selection action.

The persisted `ValidationDefinition` continues to define whether the committed value is acceptable. Runtime-specific event/focus mechanics define when that validation is evaluated and do not require target-specific Guide data.

## ADR-037 — Initial learner presentation may perform one-time target viewport adjustment

**Status:** Accepted

When a new Step target is outside the visible viewport, clipped by a scroll container, or positioned too close to a viewport edge for usable guidance, the Runtime may automatically scroll the target into a comfortable visible region before presenting the bubble.

This behavior is limited to initial presentation of the active Step. Reconciliation must not continuously re-center the target or override intentional learner scrolling.

Web may use DOM scrolling and Windows may use production-observable UI Automation scrolling such as `ScrollItemPattern` / `ScrollPattern`. The implementation must remain generic and must not depend on target application source code or private APIs.

## ADR-038 — Product UI localization is external and has no fallback

**Status:** Accepted

All user-facing DAP product UI text is loaded at runtime from external localization files shipped beside the compiled application. The active language is selected by external localization configuration, and changing wording or switching between supported language files does not require recompiling `DAP.exe`.

The localization source is singular by design:
- no hard-coded user-facing translation strings in Runtime/Application code;
- no embedded RESX translation fallback;
- no fallback from a missing key to another language or compiled default;
- a missing configuration file, language file, required key, or invalid UI direction is an explicit configuration error.

Guide instructional content remains Guide/DB data and is not product UI localization. Developer diagnostics, logs, locator strategies, validation-kind identifiers, and other internal technical text may remain compiled because they are not end-user interface copy.

## ADR-039 — TestCRM manual execution reuses the canonical E2E launch harness

**Status:** Accepted

Web and Windows TestCRM development execution use one platform runner for environment orchestration. Full human learner mode is exposed as `--manual` on the canonical Web/Windows E2E project rather than through separate PowerShell launchers.

The runner owns target-application startup, backend/browser setup where applicable, DAP launch, ports, persistence wiring, diagnostics, and owned-process cleanup. In `--manual` mode it stops synthetic learner actions once production Guide Step 1 is visibly ready; the human learner performs the entire Guide from that point.

`--manual-from-step <N>` remains the focused handoff mechanism because it preserves real preceding business state and runtime captures before automation stops at Step N.

Separate manual-launch scripts that duplicate the same startup/cleanup topology are not maintained. Product Runtime logic remains outside the E2E harness; the harness only owns development/test orchestration and synthetic learner actions.

Executable-output isolation for these unified runners is defined by ADR-040 (Windows) and ADR-041 (Web); both use unique per-run temporary output roots rather than shared repository or shared temporary executable directories.

## ADR-040 — Windows TestCRM E2E uses isolated per-run executable outputs

**Status:** Accepted

Windows Guided/Manual TestCRM execution must not run DAP, TestCRM Server, or TestCRM Windows directly from normal repository build outputs that are reused by later builds.

The Windows E2E runner builds these executables into a unique per-run directory under `%TEMP%\DAP\E2E\Windows\<run-id>` and launches them from there. The E2E project does not keep a build-time ProjectReference to `DAP.App`; DAP is built explicitly into the isolated run output.

This prevents an interrupted or orphaned learner process from locking `src/DAP.App/bin/Debug` and causing subsequent `dotnet run` builds to fail. Runner-owned child processes are cleaned up in normal `finally` handling and also on process-exit / Ctrl+C when those notifications are delivered. A hard OS termination may leave the isolated temporary directory behind, but later runs never reuse it, so it cannot block future builds.

## ADR-041 — Web TestCRM E2E also uses isolated per-run executable outputs

**Status:** Accepted

Web TestCRM execution follows the same isolation principle as Windows. Each run builds TestCRM Server, TestCRM Web, and (for guided/manual modes) DAP into a unique directory under `%TEMP%\DAP\E2E\Web\<run-id>` and launches the owned processes from that directory.

The Web runner must not use normal reusable repository build outputs as the executable location for long-lived child processes. This prevents interrupted or orphaned Web learner runs from locking the normal TestCRM or DAP build outputs and blocking later builds.

Owned-process cleanup for DAP, TestCRM Web, and TestCRM Server is registered before child startup for process-exit and Ctrl+C notifications, in addition to normal `finally` cleanup. A hard OS termination may leave the unique temporary run directory behind; future runs never reuse it.

## ADR-042 — Non-click validation uses consumable commit attempts

**Status:** Accepted

For value/text validation, a natural UI commit event represents one learner attempt rather than permanent Step completion. Text editors commit on blur after a real edit; discrete controls commit on their natural change event.

If primary validation fails, that commit attempt is consumed. The Runtime must wait for a new edit/change followed by a new commit event before it may reevaluate progression. A previously invalid commit must never leave the Step permanently armed such that later live typing can advance the Guide without another blur/change.

Web must preserve the active editor's changed-since-last-commit state across reconciliation rather than reinstalling listeners in a way that resets that state every poll. Windows must reset its text-edit baseline after consuming an invalid blur attempt. Windows text commit observation uses target-scoped UIA property events as the primary signal: `ValuePattern.ValueProperty` plus `AutomationElement.HasKeyboardFocusProperty`. Polling is only a fallback for incomplete UIA providers. Global focus-change notifications are not the commit contract because they can miss/obscure a fast target blur in the canonical flow. Canonical Windows E2E text-commit actions use real TAB focus traversal; programmatically focusing the window is not considered a faithful learner commit.

Click validation remains intentionally sticky because the validating click may immediately navigate, rerender, or destroy the source target while persisted completion conditions are still pending.

## ADR-043 — Manual runner lifetime follows the target application

**Status:** Accepted

A TestCRM `--manual` session ends when either the Guide/DAP completes or the owned target application is closed by the learner.

For Web, only explicit closure of the owned page or browser-disconnect events count as a normal operator target close. Polling `Browser.IsConnected` / `Page.IsClosed` is not the manual-run liveness contract because transport state can produce false positives. Exit of the owned TestCRM Web host is unexpected and must surface as an error. For Windows, exit of the owned TestCRM Windows process ends the manual run. The runner then cleans up the remaining processes it owns and returns control to the launching terminal.

The harness must not remain alive merely because DAP is still waiting after its guided target application has been closed. This rule applies only to runner-owned development/test topology and does not change production Runtime ownership boundaries.

This rule applies to manual, Guided, and Unguided TestCRM runner modes. Automated waits must observe the owned target lifetime so that an operator-closed target produces a clean runner stop rather than a later timeout or UIA/Playwright exception.

## ADR-044 — Web E2E readiness is process-bound and ports are preflighted

**Status:** Accepted

Canonical Web TestCRM E2E/manual execution uses ports 5200 (Web) and 5201 (backend). Before build/launch, the runner verifies that both ports are free. If either is occupied, the run fails clearly and does not kill the existing owner.

HTTP readiness alone is insufficient because a stale TestCRM process can answer the canonical URL after the newly launched host has already failed to bind. The runner therefore accepts readiness only while the exact Web-host process it launched for the current run is still alive.

This keeps startup ownership explicit: an old or unrelated process cannot impersonate the current run, and the harness never takes destructive action against an unknown port owner.



## ADR-040 — DAP-owned completion dialogs must surface in foreground

**Status:** Accepted

When DAP uses an operating-system completion dialog, successful Guide completion must be surfaced in the foreground instead of being allowed to open behind the target application.

The foreground/topmost request is scoped to the short-lived completion dialog itself. DAP must not leave a persistent Topmost application window or permanently steal foreground ownership after the learner dismisses the dialog.

This is DAP-owned product UI behavior and belongs in `DAP.App`, not in target resolution, Guide data, or TestCRM-specific code. Web's in-browser completion bubble remains the normal Web completion UI; this rule applies where DAP intentionally shows an OS completion dialog.


## ADR-043 — Canonical Web and Windows runner modes are a symmetric product contract

**Status:** Accepted

The canonical TestCRM Web and Windows runners must expose the same user-facing execution-mode vocabulary and preserve the same meaning for each shared mode wherever the platform supports that behavior.

`DAP_E2E_MODE` has only two valid mode names in the canonical contract:

- `fast` — validation-oriented execution without human/demo pacing.
- `visual` — observable learner-action pacing intended for visual inspection.

`demo` is not a supported mode name and must not be accepted or reintroduced as an alias for `visual` on either Web or Windows.

Likewise, focused-run switches such as `--manual-from-step <N>` and `--visual-from-step <N>` are cross-platform concepts: their business meaning must remain aligned even when the concrete Web/Windows automation technology differs.

Any addition, removal, rename, or semantic change to a public runner mode or focused-run switch on one platform must be reviewed and applied to the other platform in the same change, unless an explicit platform-specific exception is documented in this file.

Platform implementation details may differ — Playwright/DOM for Web and UIA/native input for Windows — but those differences must not create accidental user-facing CLI/mode drift.

Implementation note (2026-10-03): Windows now enforces `fast|visual` in the canonical runner and supports `--visual-from-step <N>`. Windows Visual uses the same UIA action path as Fast and adds visible native cursor movement/pacing; it is not a separate scenario. The standard 5-second technical timeout remains unchanged.

Runner precedence rule (2026-10-03): `DAP_E2E_MODE` is consulted only for a full `--guided` run. `--manual`, `--unguided`, `--manual-from-step <N>`, and `--visual-from-step <N>` have absolute semantics and ignore any stale `DAP_E2E_MODE` value left in the launching shell. `--manual-from-step` always uses an Unguided bootstrap through N-1 and hands off to Guided Manual at N; `--visual-from-step` always uses an Unguided bootstrap through N-1 and switches to Guided Visual at N. This rule is identical for Web and Windows.

## ADR-044 — From-Step uses Unguided bootstrap plus resumable Guide context

Focused runs do not need DAP before the requested Step. For both canonical Web and Windows runners, `--manual-from-step <N>` and `--visual-from-step <N>` therefore execute Steps `1..N-1` as an Unguided business-state bootstrap: DAP.exe is not running, no learner bubbles are presented, and production learner validation is not exercised during that prefix.

At Step N the runner starts the production learner with `--start-step N`. Runtime values captured by earlier persisted Guide Steps are carried across the boundary through an explicit resume context file. DAP validates that every supplied resume value belongs to a real earlier Step that declares a capture. WebGuideRuntime and WindowsGuideRuntime initialize their runtime-capture dictionaries from that validated context before materializing Step N or later targets.

The focused-run semantics are therefore:
- `--manual-from-step N`: Unguided `1..N-1`, then Guided Manual from N.
- `--visual-from-step N`: Unguided `1..N-1`, then Guided Visual from N.

This supersedes earlier documentation that described the prefix as Guided Fast. Full Guided Fast, full Guided Visual, full Manual, and full Unguided runs are unchanged. No E2E timeout was increased; the 5-second technical timeout policy remains in force.

### ADR-045 — Guide completion is runtime-owned and always presented

After the final Guide Step, both Web and Windows runtimes present the production DAP completion bubble and wait for its explicit Finish action. Completion is not an optional host-level message and is not controlled by a launch flag. Manual runs leave Finish to the learner; automated Guided E2E runs activate the same real completion action as a synthetic learner before asserting DAP process exit. The target business application remains open. The former `--show-completion` switch and Windows completion `MessageBox` are retired.

### ADR-046 — Centered targetless information Steps

DAP supports learner information that does not identify or act on a target as a real persisted Guide Step rather than by inventing a fake target. The canonical representation is `Target = null`, `BubblePlacement.Center`, and `StepAdvanceMode.Manual`. Such a Step cannot carry context, validation, runtime capture, or completion conditions. The Runtime displays the shared centered bubble and advances only after the learner presses the localized confirmation action.

Guide completion reuses the same centered presentation primitive with completion-specific content and Finish text. This keeps targetless information and completion presentation aligned on Web and Windows and prevents separate visual implementations from drifting.

### ADR-047 — Visual mode uses the real OS cursor

Visual mode represents an observable learner simulation and therefore uses the operating-system mouse cursor on both supported learner platforms.

- Windows Visual continues to animate the real Windows cursor to UIA-resolved controls.
- Web Visual no longer renders or animates a synthetic DOM cursor.
- Web converts Playwright target viewport coordinates to Windows screen coordinates and animates the real OS cursor with the same 12-frame cubic ease-out / 18 ms frame pacing / 120 ms dwell contract used by Windows.
- Playwright remains responsible for the Web interaction itself (click/hover/type and DOM synchronization); moving the physical cursor is the visual presentation layer, not a replacement for target resolution or validation.
- Fast mode does not animate the physical cursor.

This keeps Web/Windows Visual semantics aligned without coupling production DAP Runtime behavior to test-only cursor simulation.

Verification note (2026-10-04): ADR-047 is now locally verified in focused Visual execution on both platforms using `--visual-from-step 47`. Web uses the real Windows cursor after removal of the synthetic DOM cursor; Windows continues using the native cursor. In both runs the cursor visibly moves to the centered information `אישור` action and the completion `סיום` action before activation.


## Generality gate for fixes and E2E behavior

Before accepting a DAP product change, ask: **Could this behavior be configured or captured by the future Instructor against a closed customer application whose source code is unavailable?**

- If yes, it may be a product capability and should be represented declaratively rather than as application-specific code.
- If the behavior is needed only for the E2E harness to operate TestCRM, keep it in the TestCRM E2E driver.
- Do not modify TestCRM solely to make an E2E scenario pass when its current interaction is representative of real customer software.
- Source access to TestCRM may be used for development diagnosis only. DAP runtime behavior must not depend on customer source access.
- Do not infer learner actions globally from control type. A grid row/DataItem, for example, may require click, double-click, Enter, Space, selection only, or application-specific behavior. The future Instructor must capture/configure the intended action and its success condition.
- FAST and VISUAL are E2E execution modes, not product semantics. They should exercise the same intended learner action; VISUAL may add presentation-oriented cursor motion and delay. When an application genuinely requires a physical mouse action, FAST may perform the physical action without animated cursor travel rather than changing the target application.


## ADR-0XX — Fast and Visual share one Windows learner-action path

**Status:** Accepted

Windows E2E Fast and Visual are presentation modes over one canonical synthetic learner workflow; they are not separate test implementations.

Every application-facing action must be semantically identical in both modes. Visual may add visible cursor travel and presentation pacing only. It must not add alternate CRM navigation, alternate validation rules, target-specific synchronization, or scrolling required only to make the Visual test pass.

When the target application itself requires a physical mouse action that cannot be represented by the available UIA action pattern, that physical action remains part of the common action path. Fast may move the real cursor directly to the target; Visual may animate the same cursor movement before executing the same physical action.

Readiness and synchronization remain based on observable application/UIA state rather than arbitrary Visual-only delays. The 5-second timeout policy is unchanged.

This contract was re-verified by complete Windows Guided runs on the 54-Step canonical Guide: both Fast and Visual completed 54/54 with the same production Windows Learner Runtime. The text synchronization correction that restored Step 8 is commit `889ee17d3e34692022085760dea2496b31c0cb69`.

## ADR-048 — Fast and Visual share one cross-platform learner-action path

**Status:** Accepted

Fast and Visual are presentation modes over one canonical synthetic learner workflow on both Web and Windows. Every application-facing learner action must be semantically identical in both modes. Visual may add visible real-cursor travel and presentation pacing, but must not introduce alternate business navigation, validation rules, target-specific synchronization, scrolling, commit semantics, or a different application action merely to make Visual execution pass.

Mode-dependent typing semantics are not permitted. Web therefore uses the same text-entry action in Fast and Visual; Visual pacing belongs outside the semantic typing action. When the target application genuinely requires a physical mouse action, that physical action remains part of the common path: Fast may position the cursor immediately while Visual may animate travel before the same action.

Readiness and synchronization remain based on observable application state. Visual-only delays are presentation pacing and must not substitute for readiness. The existing 5-second technical-timeout policy is unchanged.

Verification: the complete persisted 54-Step Guided workflow has now passed in all four full Guided combinations: Web Fast, Web Visual, Windows Fast, and Windows Visual. Web action-path alignment commit: `401f31a582797fcb9217e521e40c0557196736c1`. Windows focused-value synchronization baseline: `889ee17d3e34692022085760dea2496b31c0cb69`.

This ADR generalizes and supersedes the platform-specific scope of the earlier `ADR-0XX — Fast and Visual share one Windows learner-action path`; that older entry remains as historical implementation context.



## ADR-049 — Packaged focused runners must create their own transient state root

**Status:** Accepted

A focused diagnostics runner must explicitly create every transient directory it owns before writing resume or bootstrap state. It must not rely on a repository build operation to create that directory as a side effect, because packaged diagnostics skip repository builds.

This rule was applied to Windows packaged From-Step execution after `resume-context.json` could be written beneath a nonexistent GUID run root. The fix is confined to E2E/diagnostics orchestration and does not alter production Runtime, target resolution, Guide semantics, bubbles, or the 5-second timeout policy. Fix commit: `405c1b4e577ff193be96310b8df25d6b0dc30284`.

## ADR-050 — Web first-bubble startup optimization must address Playwright initialization, not timeouts

**Status:** Accepted

Measured Production startup shows that the dominant Web first-bubble latency is `Playwright.CreateAsync()`, not SQLite initialization, Guide loading, CDP attachment, target resolution, or bubble rendering. In the measured packaged run, DAP reached Web composition at 257 ms, completed Playwright creation at 6563 ms, connected CDP at 6670 ms, and started the Web Guide Runtime at 6673 ms; first-bubble active-Step work then took 159 ms.

Playwright .NET 1.55.0 starts its packaged stdio driver process during `Playwright.CreateAsync()` and waits for initialization. The configured 10-second CDP timeout is a maximum failure bound, not the measured fixed delay.

Consequently:
- Do not increase a timeout to hide first-bubble startup latency.
- Do not add TestCRM-specific startup shortcuts.
- Do not claim that GUID-based repository output is the sole cause; the delay is reproduced from the stable Production package.
- Do not adopt a persistent/shared Playwright driver merely because it appears faster. Such a lifecycle change must first preserve deterministic ownership/cleanup, isolation, failure behavior, and support for arbitrary closed customer Web applications.

The current decision is diagnostic: the bottleneck is identified, but no speculative production optimization is accepted yet.

## ADR-051 — AI is a development aid, not a production dependency

**Decision:** Production DAP must not require an AI system for either Instructor authoring or Learner execution.

During DAP development, AI may be used freely as an engineering aid to inspect TestCRM behavior, compare before/after states, identify gaps in the model, propose general mechanisms, and accelerate implementation. This development assistance must not become a hidden runtime dependency.

The production Instructor must independently observe externally available application state, compare state before and after an author action, detect meaningful structural/state changes, rank or present candidate transition/completion conditions, and persist an explicit deterministic Guide definition. The production Learner must independently resolve targets, observe application changes, evaluate the persisted conditions, diagnose supported page/window/context transitions, and advance the Guide without AI.

The acceptance boundary for a new capability is therefore: AI may help discover or design the capability during development, but after the capability and Guide definition exist, the customer-side DAP installation must be able to author and execute the supported behavior without AI.

This decision does not prohibit a future optional AI feature. Any such feature must remain optional and must not be required for the deterministic production authoring/execution contract.


## ADR-052 — Stable Web bubble tracking is event-driven

**Status:** Accepted

Once a Web Guide Step has a uniquely resolved live target, active context, and presented bubble, the production Learner Runtime must not continuously perform full target resolution and bubble reconstruction on a fixed 100 ms cadence.

Normal steady-state presentation is browser-event-driven:
- scroll/resize and ResizeObserver maintain target-relative bubble geometry;
- validation completion events wake DAP for progression;
- DOM/context invalidation wakes DAP for fresh descriptor resolution.

The 100 ms reconciliation interval remains valid only for transient recovery states where DAP is waiting for a missing/inactive target or for short-lived persisted completion conditions after an already-observed learner action.

A child-frame bubble that requires a top-level visual proxy is an explicit exception because its page-relative geometry crosses frame boundaries. That proxy is reused rather than recreated and may use a lower-frequency 250 ms position refresh while active. If its target leaves the top-level viewport, the proxy must hide with the target rather than being clamped on-screen independently. If a normal child-frame placement becomes possible again, the stale proxy is removed.

This behavior is generic production Runtime behavior, does not depend on TestCRM source access, and does not alter the 5-second E2E timeout policy.

## ADR-053 — Hybrid automation must not become a second learner engine

**Status:** Accepted — 2026-10-06

Hybrid/E2E orchestration exists to exercise the persisted Guide and production Runtime. It must not add target-detection, state-detection, validation, completion, or progression behavior merely to make an automated run pass when that behavior is expected to be defined by persisted Guide data and evaluated by production DAP.

The persisted Guide remains the source of truth. Runtime owns Step completion and progression.

Synthetic learner actions are permitted only as representations of learner input against the same production-observable target and business flow. Persisted automation values may be used where the Guide explicitly defines them. The harness must not infer a hidden alternate completion rule from TestCRM source code, control type, or test knowledge.

This rule is supported by the successful full manual Windows execution and the subsequent successful Windows Hybrid execution on 2026-10-06. The Windows path therefore does not require Playwright or a parallel test-only learner mechanism.

## ADR-054 — Disabled Hybrid Steps preserve numbering and business semantics

**Status:** Accepted — 2026-10-06

A persisted Guide Step may be disabled when it is genuinely repetitive and skipping it does not damage the real business flow. Disabled Steps are skipped in place; Step numbers are not compacted or reassigned.

For the current canonical Hybrid workflow, Steps 24, 25, 30, 31, and 42–45 are disabled. Step 29 remains enabled because it performs a meaningful business action.

Disabling a Step is not an optimization license to bypass required behavior. Meaningful saves, validations, deletes, navigation, context creation, and state transitions required by later Steps must remain represented and executed.

Hybrid synchronization must tolerate disabled Steps while preserving Runtime ownership of progression and the original persisted Step identity.

Verification: the corrected Hybrid synchronization completed successfully with `PASS: hybrid Web Guide completed.` Relevant stabilization commits include `ac6eaaa`, `fa08a52`, and `9bb497e`.

## ADR-055 — Manual Hybrid learner waits are not bounded by the automated 5-second timeout

**Status:** Accepted — 2026-10-06

The project-wide 5-second timeout rule is a technical timeout policy for automated readiness, resolution, synchronization, and failure detection. It must not be interpreted as a deadline imposed on a human learner in Manual or Hybrid interaction.

A Manual/Hybrid run may therefore remain on an active Step while waiting for the learner without failing after five seconds. This does not increase or weaken any automated technical timeout.

The distinction is required so that production learner semantics remain human-paced while automated diagnostics still fail quickly on genuine technical faults.

## ADR-056 — E2E action drivers perform learner actions; Runtime owns outcomes

**Status:** Accepted — 2026-10-06

The 14-step Windows Hybrid migration (`e3a0adc`, `c25ebc1`, `d685124`, `c4fe463`, `782f842`, `edb732a`, `454bb55`, `91d4469`, `5290d2d`, `23ccc6b`, `53eee3b`, `8130e60`, `39519e8`, `e6a133b`) establishes a permanent ownership boundary.

An E2E/Hybrid action driver performs the configured learner action. It must not also become an independent Guide engine by polling for the same validation result, asserting the same business outcome, automatically dismissing unrelated dialogs, or deciding that a Step is complete.

Persisted Guide definitions and the production Runtime own target resolution, validation/completion observation, capture semantics used by the Guide, and Step progression. Test orchestration may wait for the production transition and may retain scenario state needed to perform later synthetic learner actions, but that state must not be used as a parallel completion oracle.

Cross-platform Hybrid code follows the same rule. Web-side action/outcome synchronization and explicit mode/browser argument handling were aligned as part of the same migration.

The completed Windows Hybrid PASS verifies this ownership model. Legacy outcome-detection and select-value-polling behavior removed by the 14-step migration must not be reintroduced into action helpers.


## ADR-057 — Guide summary is a persisted Step, not a special completion path

**Status:** Accepted — 2026-10-06

The canonical Guide summary is persisted as Step 55 on both Web and Windows. It is a targetless centered manual Step with the text `המדריך הושלם בהצלחה`.

Guide completion content must be defined by persisted Guide data and presented through the normal Runtime Step lifecycle. Canonical E2E/Hybrid orchestration may confirm that Step as a synthetic learner action, but must not wait for or operate a separate test-side completion-bubble state.

The repository seeds and `--reset-guide` must preserve this Step. The canonical seed length is therefore 55. Existing 54/54 verification records are historical and must not be rewritten as 55/55 without a fresh full regression.
