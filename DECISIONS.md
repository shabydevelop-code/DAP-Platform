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

`visual` is demonstration mode and retains cursor movement, typing delays, processing feedback, and the artificial server-thinking delay.

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

Manual pre-start of TestCRM Server or Web is not part of the normal E2E contract. This keeps Fast, Visual, CRM-only, and focused runtime validation reproducible from a single runner command.


## ADR-028 — E2E timeout increases above 5 seconds require explicit approval

**Status:** Accepted

The default maximum wait timeout for DAP/TestCRM E2E synchronization is 5 seconds.

A timeout failure must be treated first as evidence of a possible readiness, lifecycle, target-resolution, navigation, server-state, or synchronization defect. Increasing a timeout must not be used as the normal first response to a failing test, because it can hide the actual defect while only making failures slower.

Before proposing any timeout above 5 seconds, the failing transition and its real readiness condition must be investigated. An increase above 5 seconds is a last-resort change only when there is concrete evidence that the underlying operation can legitimately require more than 5 seconds.

**Any change that raises an E2E timeout above 5 seconds requires the user's explicit approval before implementation.** This applies even to temporary diagnostic changes. Polling intervals and intentionally human-paced Visual-mode delays are separate concerns and do not override this rule.


## ADR-029 — Unguided reuses the canonical persisted Guide sequence (historically CRM-only)

**Status:** Accepted

Web Unguided is the canonical CRM business flow without `DAP.exe` and learner bubbles. It is not a separate TestCRM QA scenario.

Normal Guided Web execution and Unguided both consume the persisted `testcrm-web-canonical-workflow` Guide from the configured DAP data provider and follow the same 53-Step sequence. Guided mode additionally synchronizes with production DAP Runtime/bubble state; Unguided omits that presentation/runtime synchronization.

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

The canonical TestCRM Web and Windows Guides each contain 53 persisted Steps and represent the same business workflow. Runtime-specific target technology may differ, but Step order, learner intent, business identity, and progression semantics must remain aligned.

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

## ADR-040 — Windows TestCRM E2E uses isolated per-run executable outputs

**Status:** Accepted

Windows Guided/Manual TestCRM execution must not run DAP, TestCRM Server, or TestCRM Windows directly from normal repository build outputs that are reused by later builds.

The Windows E2E runner builds these executables into a unique per-run directory under `%TEMP%\DAP\E2E\Windows\<run-id>` and launches them from there. The E2E project does not keep a build-time ProjectReference to `DAP.App`; DAP is built explicitly into the isolated run output.

This prevents an interrupted or orphaned learner process from locking `src/DAP.App/bin/Debug` and causing subsequent `dotnet run` builds to fail. Runner-owned child processes are cleaned up in normal `finally` handling and also on process-exit / Ctrl+C when those notifications are delivered. A hard OS termination may leave the isolated temporary directory behind, but later runs never reuse it, so it cannot block future builds.

