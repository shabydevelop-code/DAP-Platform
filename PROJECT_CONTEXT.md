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
