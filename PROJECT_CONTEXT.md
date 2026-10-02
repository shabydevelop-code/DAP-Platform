# Project Context

## Verified four-mode persisted-Guide baseline — 2026-10-02

The current canonical execution modes are **Guided** and **Unguided**. The old `CRM-only` naming is historical and must not be used for the current mode contract.

Locally verified baseline:
- **Web Unguided — PASS 53/53.** Executes the 53 persisted Steps from `testcrm-web-canonical-workflow` through the E2E action executor without DAP.exe/bubbles.
- **Web Guided — PASS 53/53.** Executes the same persisted 53-Step Guide through DAP.exe, the production Web Runtime, real target resolution, validation, and learner bubbles.
- **Windows Unguided — PASS 10/10.** Executes the 10 persisted Steps from `testcrm-windows-canonical-workflow` through the Windows E2E action executor without DAP.exe/bubbles.
- **Windows Guided — PASS 10/10.** Executes those same 10 persisted Steps through DAP.exe, the production Windows Runtime, real UIA targets, validation, and learner bubbles.
- The separate Windows canonical 53-step business-flow harness is also locally verified PASS, but **DAP Windows Guided/Unguided persisted-Guide coverage is currently 10 Steps, not 53**.

Recent Windows Runtime findings/fixes now part of the verified baseline:
- Repeated/grid targets must resolve uniquely. The Windows Guide Step for the site row uses contextual identity rather than silently choosing the first matching `DataItem`: the target is scoped by the `SitesGrid` ancestor and the intended site identity.
- Multiple anchors are supported by the existing runtime-neutral `TargetDescriptor`; ambiguity remains an explicit resolution result and the Runtime must not guess.
- The persisted Guide is the source of learner bubble content. Guided E2E reads expected bubble content from the Guide loaded from `DAP.db` instead of duplicating instruction strings in the harness.
- Windows navigation actions synchronize on their rendered destination screen before the next learner action proceeds.
- A UIA target can exist before it has usable visible bounds. Windows Runtime reconciliation now waits until a resolved target is visible and has non-empty bounds before presenting its bubble instead of crashing.
- For Windows `clicked` validation, completion can be observed either through the invoke event or when a previously resolved/visible activated target leaves the UI during the resulting navigation/re-render. A target that has never resolved does not satisfy `clicked`.
- The Windows E2E action waits remain bounded by the 5-second policy; the fixes did not increase learner/action timeouts.
- Guide seed changes do not silently overwrite an existing persisted Guide. Test Guide updates are applied explicitly with `--reset-guide`, preserving the rule: **Seed initializes. DB owns. Runtime consumes.**

Next Windows milestone: expand `testcrm-windows-canonical-workflow` from the verified 10 persisted Steps toward the representative 53-step business workflow while preserving the four-mode baseline.


## Product

DAP Platform is a production-target Digital Adoption Platform for creating and running interactive guides across Web and Windows applications.

The architecture documented in this repository is the product architecture. It must not be described or implemented as a proof of concept.

## Application

The product is a single Windows desktop application with two user modes:

- Learner — discovers, starts, continues, and completes guides.
- Editor — creates, records, edits, previews, and manages guides.

The application must not couple the core guide model to the GUI technology.

## Closed-target / black-box rule

- DAP must never require access to the source code, internal implementation, database, private APIs, or other privileged internals of the application being guided.
- Production Learner Runtime and Instructor/Editor capabilities must work with closed third-party applications as black boxes, using only information that DAP can legitimately observe or interact with externally through the applicable runtime technology (for example UIA for Windows and browser/runtime-visible information for Web).
- Source code of DAP.TestCRM may be inspected during DAP development to diagnose failures, understand how observed UI/runtime behavior is produced, learn patterns likely to occur in closed applications, and distinguish a fixture defect from a generic DAP limitation.
- **Source inspection is allowed for diagnosis and learning; it is not allowed as a resolver/runtime oracle.** Knowledge obtained from source code must never be required at runtime, encoded as a target-specific shortcut, or used to bypass what DAP could discover from production-observable interfaces.
- Every fix derived with help from TestCRM source inspection must be implemented as a generic production capability that remains valid when the target application's source is unavailable.
- TestCRM should deliberately expose difficult real-world black-box conditions so these limitations are discovered during development rather than at customer deployment.
- When practical, Guide creation and runtime validation should also be exercised under a strict black-box perspective: if DAP cannot discover enough information through its production-observable interfaces, treat that as a product capability gap rather than solving it through target-source knowledge.

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


## Current runtime implementation scope

The shared architecture and Core contracts are designed from the start for both Web and Windows runtimes. Runtime-neutral models such as TargetDescriptor, Locator, Anchor, Validation, Bubble, GuideStep, and related contracts must therefore avoid Web-only or Windows-only assumptions unless represented through an explicit runtime-specific extension/adapter.

The current implementation phase, however, is Web-only: build and validate the Learner Web Runtime and Web bubbles using Microsoft Playwright for .NET and DAP.TestCRM. Do not implement Windows bubble rendering or the UIA runtime during this phase. Windows will later consume the same shared Core contracts through DAP.Runtime.Windows.

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


## Non-negotiable test fidelity rule
- DAP tests and Guide fixtures adapt to the target application; the target application must not be changed merely to make a DAP test or Guide pass.
- Do not alter TestCRM business content, seed records, labels, workflow behavior, DOM semantics or application behavior solely to remove a DAP targeting/testing difficulty.
- A target-application change is allowed only when it is independently required by the target application's product/demo scenario, not as a workaround for DAP.
- Target ambiguity must be solved in DAP targeting semantics (for example stable locators, multiple anchors, context or other production-capable identity), or exposed as a genuine limitation. Never manufacture uniqueness in the target system for the test.


## Runtime-created business identity
- A Guide may need to revisit an entity whose stable identity is created during the learner session (for example a CRM Case ID returned after Save).
- Do not modify the target application or manufacture test-only DOM metadata to make such targets unique.
- Web Guide locator/anchor values may reference a previously captured Step frame URL fragment with `{{step:<step-id>:frame-url-fragment}}`.
- Capture is transient learner-runtime state, not persisted business data and requires no SQLite schema change.
- Only Steps referenced by such tokens are navigation-sensitive capture sources. Before capture, the Web Guide Runtime waits for their target frame to leave the preceding Step URL, preventing a validating click from capturing the pre-navigation route.
- Runtime values are materialized into a per-run GuideStep copy; persisted Guide definitions remain unchanged.
- TestCRM Steps 12 and 50 use the real existing `data-go` route plus the runtime-captured Case fragment. TestCRM itself is not changed for DAP targeting.


## Persistent Guide database rule
- The TestCRM visual system demonstration uses the same persistent DAP SQLite database as DAP.exe: `SqliteDatabaseOptions.CreateDefault()`, normally `C:\ProgramData\DAP\Data\DAP.db`, with `DAP_DATABASE_PATH` remaining the supported explicit override.
- Do not hide the production Guide used by the visual demonstration in a random per-run temporary database.
- The E2E harness may seed/update the known TestCRM Guide fixture in the persistent DAP database, then must launch DAP.exe against that exact database path.
- This makes persisted Guides/GuideSteps/TargetAnchors inspectable and keeps the visual demonstration aligned with the real DAP persistence boundary.
- Isolated temporary databases remain appropriate for dedicated repository/unit tests where persistence isolation is the subject of the test.


## Guide persistence ownership — Seed initializes, DB owns, Runtime consumes
- **Persistent DAP database is the Source of Truth for Guides once initialized.**
- A Guide seed/factory is an initialization/reset definition only. It must never silently overwrite an existing persistent Guide during normal Learner, E2E, or visual-demo execution.
- Normal execution flow: open DAP database -> load persisted Guide -> run exactly that persisted Guide.
- If the required Guide is absent, normal E2E must fail clearly and require an explicit initialization/reset operation; it must not silently seed.
- Future Instructor/Editor changes are written to the database and immediately become authoritative for Learner and E2E.
- Keep a known factory-default Guide definition so an explicit reset can restore a known baseline when requested.
- Dedicated isolated repository/unit tests may still seed their own temporary databases because their purpose is persistence testing, not product Guide ownership.
- Product rule: **Seed initializes. DB owns. Runtime consumes.**


## Guided transition vs technical E2E actions
- After a learner action completes a Guide Step, E2E must observe the next production Guide Step before performing any technical scenario action that can replace/reload the guided document.
- Technical reload/frame-lifecycle scenarios may then run and must verify that the already-active Step is reconciled/re-presented afterward.
- This prevents the harness from racing DAP's event-driven Step transition and preserves the invariant that Guide progression is driven by learner actions, not test timing.


## Click validation navigation durability
- A Web `clicked` completion must reach DAP.exe before a cancelable browser default action is allowed to destroy/navigate the source document.
- Fire-and-forget Playwright binding calls are not considered durable merely because the binding is re-exposed after navigation; an in-flight call can race document teardown.
- For anchors and form submit controls with cancelable browser default actions, the presenter temporarily prevents that default action, awaits DAP's validation binding acknowledgement, then replays the native navigation/submission action.
- Application event handlers are not replaced by DAP. This mechanism only gates cancelable browser default behavior and does not tailor the target application.


## Validated full 53-Step Web Guide baseline — 2026-10-01
- Fast E2E now completes the full persisted 53-Step TestCRM Guide and all ten representative PeopleSoft-style scenarios.
- Verified terminal result: `PASS: representative Customer -> Site -> Case -> Lead workflow, including dynamic Lead deletion and Case deletion, completed.`
- Fast mode exposed a production lifecycle defect when the same live DOM element was reused by later Guide Steps. Validation listeners were previously effectively element-owned and could retain the earlier Step ID.
- Concrete evidence: Step 16 (`testcrm-case-closed`) initially reported completion for Step 13 (`testcrm-case-in-progress`), and Step 21 (`testcrm-save-closed-case`) reported completion for Step 18 (`testcrm-attempt-close-save`).
- WebBubblePresenter now treats both value-validation and click-validation handlers as Step-owned lifecycle state: when a reused target is presented for a later Step, the previous handler is removed and a handler bound to the active Step is installed.
- This is a generic Web Runtime fix, not a TestCRM workaround. No target-application behavior or business data was changed.
- Guide lifecycle diagnostics remain available in E2E timeout output through `[DAP guide]`, `[DAP validation]`, `[DAP bubble]`, and `[DAP runtime]` lines.
- Current regression baseline: the full 53-Step Fast E2E must remain passing.

## Validated Web browser baseline — 2026-10-01

The Learner Web Runtime representative 53-Step DAP.TestCRM E2E is validated on Playwright Chromium, installed Google Chrome, and installed Microsoft Edge. The E2E runner selects the browser with `DAP_E2E_BROWSER=chromium|chrome|edge`, with Chromium as the default. The same production Runtime and persisted Guide are used across all three browser runs, and the 5-second E2E default timeout remains unchanged.

Cross-browser navigation durability is a Runtime responsibility. Application readiness must be based on the current live application/frame readiness contract rather than a browser lifecycle event that may already have completed before a waiter is registered. Likewise, click completion is DAP-owned state: when completion is reported while the validating action is replacing/navigating its document or frame, Learner reconciliation must be able to observe that completion and advance without depending on another operation against the retiring document. Browser-specific or target-application-specific timing workarounds are not acceptable substitutes for this behavior.

## Instructor Target Capture vs Learner Runtime robustness

The cross-browser click/navigation race fix belongs to the Learner Web Runtime engine. It is not Guide-specific data and must not be encoded as a TestCRM, browser, or individual Guide workaround. Runtime responsibilities include surviving DOM/frame replacement, re-resolving targets, observing DAP-owned validation completion, and advancing safely when the source document is retiring.

A separate risk exists when a newly authored Guide captures insufficient or unstable target identity. A robust Learner Runtime cannot infer the author's intended target if the persisted definition is inherently weak or ambiguous. Therefore the future Instructor/Editor Target Capture flow must capture and persist a rich runtime-neutral `TargetDescriptor`, not merely a single CSS selector.

Target Capture should collect candidate identity evidence appropriate to the runtime, including the primary locator, stable attributes/identity signals, frame hierarchy/context, useful anchors, and applicable business/screen context. Multiple anchors must be supported when one locator/anchor is not sufficient for unique identification.

The Instructor must validate the captured descriptor using the same production target-resolution semantics consumed by the Learner Runtime. At authoring/preview time the expected result is exactly one `Resolved` target. A `NotFound` result means the captured identity is insufficient or no longer valid; an `Ambiguous` result means additional identity/context/anchors are required. The Instructor must not silently accept ambiguity by selecting the first match.

Architectural separation:
- **Instructor/Editor responsibility:** capture enough stable target evidence and validate that the persisted descriptor identifies the intended target uniquely.
- **Persistence responsibility:** store the runtime-neutral descriptor, ordered anchors, frame/context information, and related Step definition without coupling Core to a database provider.
- **Learner Runtime responsibility:** consume that persisted identity, re-resolve it against the current live application state, tolerate lifecycle races and DOM/frame replacement, and return explicit resolution failure/ambiguity rather than guessing.

This separation is required so Guides created in the future benefit automatically from engine-level lifecycle fixes while target-quality problems are detected during authoring instead of being hidden by Guide-specific runtime workarounds.

## Learner surface and unavailable-target policy — 2026-10-01

The current product direction is intentionally minimal for learners. DAP Learner is primarily a Runtime, not a management dashboard. A learner is expected to launch a specific Guide from an organization-provided icon, shortcut, portal, or other distribution mechanism; DAP then runs that Guide and presents its in-application bubbles. DAP should not duplicate an organization's existing application/shortcut distribution or authorization surface unless a future product requirement explicitly calls for an optional launcher/catalog.

Do not add a persistent Learner dashboard or generic between-Step progress/loading indicator to the current baseline. Between bubbles the Runtime should remain visually quiet and continue target re-resolution. This avoids visually competing with or impersonating the target application's own loading and status UI. If later user testing demonstrates a need for learner-visible waiting/error UX, treat that as a separate product decision rather than a prerequisite for Web Runtime correctness.

A NotFound target is not sufficient evidence that a Guide is broken. The same observation can mean that the intended element is temporarily absent during server processing, FieldChange, navigation, iframe replacement, conditional rendering, or a genuinely invalid/stale Guide. Therefore the Runtime must not automatically skip, select a similar element, or declare failure solely from a short fixed target wait. The unresolved case where the intended target never appears remains an explicit future learner-UX/diagnostics decision.

## Repeated collections, Grid identity, and author intent

Selecting a DOM element during Instructor capture does not fully define its runtime identity, especially inside a repeated collection or Grid. The future Instructor must preserve the author's identification intent rather than assuming that the element's current DOM position is its identity.

At minimum, repeated/Grid capture must distinguish:
- **Positional identity:** for example, "the first row" or "the cell in the first row." Reordering may intentionally change which business record is targeted.
- **Business/content identity:** for example, "Case Number = 7." Reordering must not change the business record targeted; the Runtime must re-resolve the matching record at its new position.

The author may select the business-key cell itself (for example the cell containing Case Number 7), or select another control in that record's row (for example an Open button). In the latter case the business-key evidence can act as an anchor/context constraint for resolving the actual target within the same repeated record. Do not reduce this to unrestricted text matching such as generic has-text("7"); target evidence should be scoped to the appropriate collection/record/field structure so unrelated text cannot satisfy the identity accidentally.

These are authoring semantics, not TestCRM/Grid-specific Runtime concepts. Core/Runtime should continue to operate through runtime-neutral target descriptors, locators, anchors, frame/context constraints, and explicit Resolved / NotFound / Ambiguous outcomes. Before building the Instructor UI, verify that the shared TargetDescriptor model can cleanly represent both positional and business-identity cases without introducing target-application-specific concepts.


## Current Web bubble presentation and manual-debugging rules — 2026-10-02
- Bubble presentation is allowed to use a different document surface from the resolved target when a child iframe physically cannot display the bubble. Target identity and validation remain bound to the original frame/element; only the visual bubble is promoted to the top-level page.
- A promoted bubble is a real interactive Learner surface, not a passive diagnostic proxy. It must preserve the same learner-facing interaction semantics as a normal bubble.
- Draggable Learner bubbles use an explicit `⠿` handle. Only that visible handle starts dragging. Normal bubble content must not advertise drag affordance. Cursor contract: normal content = default, handle hover = `grab`, active drag = `grabbing`.
- Guide completion is an explicit learner state presented as a completion bubble with a `סיום` button. Completion must wait for the learner's real click; it must not synthesize the click. Finishing the Guide is conceptually separate from closing the target business browser.
- Stateful manual debugging must preserve the real prior workflow. The E2E `--manual-from-step <N>` handoff is therefore generic: automate Steps before N using the representative scenario and pause when Step N is ready, rather than reconstructing synthetic state or adding Step-specific branches.
- Step-specific presentation workarounds are not acceptable. The constrained-iframe promotion and manual handoff mechanisms must remain generic runtime/test-harness capabilities.

## GitHub repository access

- Canonical repository: `shabydevelop-code/DAP-Platform`.
- Connected GitHub account `shabydevelop-code` has verified repository permissions: `admin=true`, `maintain=true`, `pull=true`, `push=true`, `triage=true`.
- In new chats, do not assume repository access is read-only. When write capability matters, verify permissions from repository metadata before concluding that write access is unavailable.
- DAP development may read and write this repository through the connected GitHub tools unless verified repository permissions change.


## TestCRM refactor verification and database separation (2026-10-02)
- The TestCRM refactor to `Server/`, `Web/`, and `data/` was pulled and verified locally.
- TestCRM starts successfully on `http://localhost:5200` after the WebRoot fix.
- The representative `Customer -> Site -> Case -> Lead` E2E workflow passes after the refactor, including dynamic Lead deletion and Case deletion.
- `tests/DAP.Data.Sqlite.Tests` contains infrastructure tests for DAP's SQLite persistence layer; it is not a TestCRM application/test-data directory and should remain under `tests/`.
- DAP product persistence and TestCRM business persistence are intentionally separate:
  - DAP product data: default `%ProgramData%\\DAP\\Data\\DAP.db` (or `DAP_DATABASE_PATH`).
  - TestCRM business data: `test-apps/DAP.TestCRM/data/testcrm.db`.
- Do not merge these databases: TestCRM is an external target/demo application, while `DAP.db` stores DAP guides, steps, targets, bubble/validation configuration, and related product state.


## Source-edit safety rule
- When programmatically editing source files, never insert escape sequences such as \`\\n\`, \`\\r\\n\`, or \`\\t\` as literal source text when actual whitespace is intended.
- After an automated source edit that introduces or replaces line breaks/whitespace, re-read the edited region before considering the change complete.
- Explicitly verify that no unintended literal escape sequences remain and that the surrounding source structure is syntactically intact.

## Web E2E unified modes and Windows parity review — 2026-10-02
- The representative Web E2E project is now `tests/DAP.TestCRM.Web.E2E/DAP.TestCRM.Web.E2E.csproj`.
- Web E2E execution uses one canonical Customer -> Site -> Case -> Lead business flow. Mode switches must not create a second independently maintained business scenario.
- `--crm-only` runs that canonical CRM flow without initializing/reading DAP.db, without loading the Guide, and without launching DAP.exe. This CRM-only baseline was locally verified through the terminal PASS.
- The normal Fast + DAP path was locally re-verified after the unified-mode refactor: the persisted Guide loaded 53 Steps and the complete representative workflow ended in PASS.
- `--manual-from-step <N>` preserves its existing semantics: execute the real preceding workflow in Fast mode, wait until production Guide Step N is visibly ready, then stop automation and hand control to the human tester. Step 47 handoff was locally verified.
- Added `--visual-from-step <N>`: execute Steps before N in Fast mode, switch the same running canonical scenario to Visual at Step N, and continue automatically to the end. `--manual-from-step` and `--visual-from-step` are mutually exclusive.
- Local verification of `--visual-from-step 47` showed the explicit `FAST -> VISUAL` transition at Step 47 and completed the workflow with terminal PASS.
- Visual frame-replacement waiting no longer requires observing the transient `#content-frame-next` Attached state. The harness waits for the stable replacement outcome/current ready Content frame, avoiding a race where the transient frame can be created/promoted before Playwright observes it. The Step-47-to-end Visual verification passed through the previously failing Step 50.
- In full Visual mode the final Step 53 bubble is intentionally left visible briefly before the automated final action so the final Guide instruction can be observed.
- Current Web execution behaviors are therefore: full Fast, full Visual, Fast -> manual at N, Fast -> Visual at N, CRM-only, full manual Learner launcher, and explicit Guide reset. Browser selection remains `chromium|chrome|edge` where applicable.
- Windows TestCRM was reviewed against the persisted 53-Step Web Guide. Most of the business workflow is relevant to Windows because both clients use the same server/API/business model, but Web-specific mechanics (DOM/iframe/frame URL/CSS targeting) must not be copied literally into Windows UIA tests.
- Known Windows parity gaps before building the Windows CRM-only E2E: Case Resolution Notes is displayed/enabled but is not currently persisted in `CaseInput`; there is no Windows equivalent of the Web Step-15 activity-more interaction; and the Web Step-6 explicit Cases sort behavior does not currently have an equivalent explicit Windows implementation.
- Windows validation/delete confirmations currently use WPF `MessageBox`, which is a valid platform-specific equivalent rather than the Web PS alert/confirm DOM. Dynamic Lead status behavior and conditional Selected Service UI are present and are suitable for equivalent Windows business-flow coverage.
- Planned order remains: close required Windows CRM parity gaps -> build a Windows CRM-only UI Automation E2E against the real WPF client -> stabilize/PASS the business scenario -> only then integrate DAP Windows Runtime/target resolution/bubbles.



## TestCRM client/server separation — 2026-10-02

TestCRM is development/test infrastructure only and is never part of a customer DAP production package.

The test application now has explicit deployment boundaries:
- `Server/DAP.TestCRM.Server.csproj` — shared API, business rules, validation, FieldChange behavior and `data/testcrm.db`. It contains no Web static UI.
- `Web/DAP.TestCRM.Web.csproj` — Web host and `wwwroot` static UI only. It proxies `/api` to the shared backend.
- `Windows/DAP.TestCRM.Windows.csproj` — WPF client. It calls the shared backend directly and has no dependency on the Web client.

Development ports:
- shared backend: `http://localhost:5201`
- Web host: `http://localhost:5200`

Architectural invariant: Web and Windows may depend on the shared backend contract, but neither client may depend on the other client. A Windows-only test deployment must work with Server + Windows after the Web directory is absent; a Web-only test deployment must work with Server + Web after the Windows directory is absent.

The old combined root `DAP.TestCRM.csproj` and root launch profile were removed so the Web static files cannot accidentally become a backend dependency.


## Windows CRM-only canonical E2E baseline — 2026-10-02
- The Windows CRM-only UI Automation E2E is implemented in `tests/DAP.TestCRM.Windows.E2E` and executes the shared canonical core scenario from `tests/DAP.TestCRM.E2E.Common` against the real WPF TestCRM client.
- Local verification reached terminal PASS: `PASS: Windows CRM-only canonical Customer -> Site -> Case -> Lead core scenario completed.`
- The verified core covers Customer -> Site -> Case -> Lead navigation, Case creation/save/status FieldChange behavior, Resolution Notes enablement, required-field validation handling, Lead creation/save/status transitions, conditional Selected Service handling, Lead deletion confirmation, and return to Portal.
- WPF ComboBoxes used by the scenario expose a custom UIA ValuePattern through `AutomationComboBox`, allowing deterministic value selection without physical popup interaction.
- Case/Lead FieldChange responses are applied locally in the WPF client instead of immediately reloading the persisted record, preventing transient status changes from being reverted before Save.
- Windows E2E navigation now synchronizes on the rendered destination screen rather than assuming an async click has completed. Site breadcrumb targeting uses stable UIA identity (`AutomationId=Breadcrumb` plus the site name), and Lead creation retries safely until the form is ready.
- Modal WPF MessageBoxes are handled as real platform dialogs. Expected validation/delete dialogs are dismissed/confirmed explicitly; unexpected informational OK dialogs block further E2E actions until dismissed so the scenario cannot continue behind a modal window.
- Default Windows E2E wait timeout is 5 seconds.
- The activity-more interaction remains a Windows-specific UI area to align with the Web behavior; the canonical scenario no longer incorrectly calls validation dismissal immediately after `ShowMoreActivity`.
- This PASS establishes the Windows CRM-only core baseline. It does not yet mean that the complete persisted 53-Step Web Guide has been reproduced in Windows; the next parity work is to extend the shared/core coverage toward the remaining canonical business steps before integrating DAP Windows Runtime bubbles/target resolution.

- Windows/Web activity-more parity correction: Web renders `#activity-more` but defines no click handler or alert for it. The Windows-only `MessageBox` ("אין פעילויות נוספות להצגה.") was therefore removed; `ActivityMoreButton` now has the same no-alert/no-op business behavior as Web. Commit baseline follows the already verified Windows core PASS; local rerun is required after pull.
- Pending Windows parity milestone remains explicit: expand the current Windows CRM-only shared core scenario to cover the complete canonical 53-step business workflow before DAP Windows Runtime bubble/target integration is considered complete.

- **Windows canonical 53-step CRM-only milestone: PASS (locally verified 2026-10-02).** `CanonicalCrmScenario.Run53Async` completed the full Customer -> Site -> Case -> Lead flow and terminated with `PASS: Windows CRM-only canonical 53-step Customer -> Site -> Case -> Lead scenario completed.` The Windows E2E now synchronizes async sort/create/delete/navigation transitions in the test harness. Test-only row AutomationIds were removed from the Windows target application; E2E target resolution remains the responsibility of the test harness. No full-53 PASS is claimed for DAP Runtime/bubbles yet; this milestone is the CRM-only Windows business-flow baseline.


## Guide persistence identity and Web E2E baseline — 2026-10-02
- DAP persistence uses numeric internal IDs for Guides and GuideSteps, with stable textual keys stored separately in `Guides.Key` and `GuideSteps.Key`.
- Existing legacy SQLite databases using TEXT primary keys are migrated automatically by `SqliteDatabaseInitializer` while preserving Guide/Step keys, ordering, bubbles, validation, context, frame data and anchors.
- The canonical TestCRM Web Guide key is `testcrm-web-canonical-workflow`; the previous `testcrm-create-case` key is migrated in place.
- The canonical Web Guide currently contains 53 persisted Steps in the DAP database.
- Local verification on 2026-10-02 completed the full Web runtime workflow after the schema migration with terminal PASS: `PASS: representative Customer -> Site -> Case -> Lead workflow, including dynamic Lead deletion and Case deletion, completed.`
- The normal Web E2E runner is self-contained: it starts the shared TestCRM backend on port 5201 and the Web host on port 5200, waits for readiness, runs the browser/DAP scenario, then terminates only the processes it owns.
- The next runtime milestone remains Windows Learner Runtime integration against the same DAP persistence architecture, beginning with real Steps 1–2 and real bubbles.

## Canonical E2E execution modes

The canonical Web test Guide is `testcrm-web-canonical-workflow` with 53 persisted Steps in the configured DAP database. Both execution modes consume that same persisted Guide and the same canonical CRM business flow:

- Normal/guided mode launches `DAP.exe` and verifies production Web Runtime behavior, bubbles, validation, and Guide progression.
- `--crm-only` omits `DAP.exe` and bubble synchronization but remains sequenced by the same 53 persisted Guide Steps. It is not an independent TestCRM QA script.

Both modes are locally verified PASS on 2026-10-02 after the CRM-only Guide sequencing work. The Guide remains the source of learner sequence/targets/validation/context; synthetic E2E input values that are intentionally not encoded by generic Guide validation remain test-fixture concerns.

## Windows Learner Runtime status

Production Windows runtime code exists in `src/DAP.Runtime.Windows`. It uses UI Automation for target resolution and validation and WPF for non-activating learner bubbles. `DAP.exe` supports `--learner-windows <guide-key> --window-automation-id <id>` and consumes persisted Guide Steps from the same provider-independent persistence boundary. The first 10 persisted Windows TestCRM Steps are locally verified end-to-end in Guided mode through the production runtime and in Unguided mode through the persisted-Guide action executor. The separate TestCRM Windows 53-step business scenario remains the expansion baseline while persisted production-runtime coverage grows from 10 toward 53 Steps.

## Windows persisted Guide Steps 1–12 — verified 2026-10-03

The persisted Windows Guide `testcrm-windows-canonical-workflow` is now locally verified through **Step 12** in Guided mode with the production DAP Windows Learner Runtime. The terminal run passed with real UIA targets, runtime capture, and learner bubbles.

The new dynamic Case-row flow is:
1. create and save a Case;
2. capture its generated identifier from the live Case screen;
3. return to the Case list;
4. resolve the row whose descendant identifier equals the captured value;
5. present the learner bubble on that row and open it.

For small already-realized Windows grids, row targeting deliberately follows the same simple model used by the working Sites grid: scope to the declared grid, enumerate `DataItem` rows, filter each row by the declared descendant anchor, and require uniqueness. More specialized anchor-first/grid-provider paths remain available for larger scopes, but the Runtime must not use TestCRM source or database knowledge as an oracle.

The TestCRM server now bounds accumulated Site-1 Cases to the newest 10 on startup when the fixture DB already exists. This keeps repeated E2E runs deterministic and prevents historical test data from turning a small-grid learner scenario into a performance artifact.

Windows learner bubble presentation now includes the same key interaction conventions as Web: explicit drag handle, directional pointer toward the target, and initial placement that attempts to avoid covering the target. The implementation remains Windows-native WPF and derives target geometry from UIA bounds.

The Windows E2E action driver now selects a dynamically located WPF DataGrid row with `SelectionItemPattern` before using the required physical double-click fallback. This is test-driver synchronization, not production target-resolution behavior.

## Windows learner bubble manual placement — 2026-10-03

Windows bubble behavior now matches the corresponding Web interaction more closely. A learner may drag the bubble using the explicit drag handle. Once dragged, the bubble remains at that manual location while the same Guide Step remains active, even though the Windows Runtime continues its normal target reconciliation loop. The pointer is hidden after the drag completes.

Manual placement is Step-scoped, not global. Entering a new Step clears the manual-position flag, restores automatic target-relative placement, and shows the directional pointer again. This behavior changes only bubble presentation; target resolution and validation remain unchanged.
