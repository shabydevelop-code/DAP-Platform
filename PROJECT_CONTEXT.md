# Project Context

## Verified four-mode persisted-Guide baseline — 2026-10-03

The canonical execution modes are **Guided** and **Unguided**. The old `CRM-only` naming is historical and should not be used for the current mode contract.

Locally verified baseline:
- **Last fully verified canonical baseline:** Web Guided/Unguided and Windows Guided/Unguided all passed the previous 53-Step Guides.
- **Current repository seeds:** both canonical Guides now define 54 Steps after adding a centered information pause at Step 51.
- **54-Step status:** both Web and Windows Guides have now been explicitly reset to 54 Steps. Focused `--visual-from-step 47` is locally verified PASS on both platforms, including the centered information Step and completion. Do not claim a full four-path 54/54 PASS until fresh full Guided/Unguided regressions are run.
- Windows Guided was re-verified again on 2026-10-03 after the text-commit regression work: all 53 persisted Steps completed through the production Windows Learner Runtime with real UIA targets, runtime capture, modal targeting, and bubbles. The former Step-8 `CaseSubject` stall is closed.

Both canonical repository seeds now contain 54 Steps and represent the same Customer -> Site -> Case -> Lead business scenario. Existing persisted Guides can remain at the previous 53-Step version until explicitly reset:
- `testcrm-web-canonical-workflow`
- `testcrm-windows-canonical-workflow`

Current parity rules:
- business identity must be explicit when row order is not semantically meaningful;
- `מטה תל אביב` and `אבי כהן` are selected by identity rather than "first row";
- the Case created during the active run is captured and reopened by that runtime identity;
- progression-critical destination/state rules belong in persisted Guide completion semantics;
- Runtime owns evaluation mechanics through production-observable Web/UIA interfaces;
- E2E remains only the synthetic learner;
- source inspection is allowed for diagnosis and learning, never as Runtime/resolver oracle.

Windows bubble behavior is now part of the verified baseline: target highlighting, target-relative positioning, explicit-handle dragging, preservation of manual relative offset during move/resize, hide on minimize/foreground loss, and restore from current UIA geometry.

Current full manual learner execution:
- Web: `dotnet run --project tests\DAP.TestCRM.Web.E2E\DAP.TestCRM.Web.E2E.csproj -- --manual`
- Windows: `dotnet run --project tests\DAP.TestCRM.Windows.E2E\DAP.TestCRM.Windows.E2E.csproj -- --manual`

The canonical E2E runners build into isolated per-run temporary outputs rather than the repository's normal build directories. In `--manual` mode they perform no synthetic learner actions after Step 1 is ready; the human user follows the persisted Guide through the production runtime.

Current cross-runtime UX parity rules:
- Text editing completes on natural commit, not on the first intermediate valid character. Web uses blur after a real edit; Windows requires an observed edit followed by focus loss before value validation may advance.
- Discrete controls commit on their normal selection/change action.
- A newly presented target may be auto-scrolled once into a comfortable visible region. Reconciliation must not repeatedly force viewport position after that initial presentation.
- Manual bubble dragging makes manual placement authoritative for the active Step. The directional pointer disappears immediately when dragging begins and remains hidden until the Step changes.
- TestCRM Windows Case sorting is exposed through the `סטטוס` grid header, matching the Web learner interaction instead of using a separate learner-facing sort button.
- Web completion is the in-browser DAP completion UI; the Web `--manual` runner must not add a duplicate OS completion dialog.
- DAP-owned OS completion dialogs must request foreground presentation when shown so the learner cannot miss successful completion behind the target application. The request is scoped to the completion dialog lifetime; it must not leave persistent Topmost state. This behavior was manually verified on 2026-10-03.

Web FieldChange synchronization now waits for the actual replacement document identity before accepting readiness, preventing stale-document races.

The 5-second E2E timeout policy remains unchanged. Guide seed updates require explicit `--reset-guide`; normal execution follows **Seed initializes. DB owns. Runtime consumes.**

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

- Web learner production path: Manifest V3 browser extension + Native Messaging bridge + `.NET ExtensionWebBrowserAdapter`.
- Web regression/compatibility baseline: Microsoft Playwright for .NET behind the same browser-adapter contract.
- Windows: Microsoft UI Automation (UIA).
- Desktop GUI: .NET 8 + WPF.

The production application must not require Python. The Web extension is a browser adapter; the .NET Runtime remains authoritative for learner/Guide policy. Playwright remains the behavioral baseline during migration but is not the `DAP.exe --learner-web` production composition.

## Localization

DAP product UI localization is runtime-loaded from external JSON files shipped beside the compiled application:

```text
Localization/
  language.json
  he.json
  en.json
```

`language.json` selects the active product UI language. The selected JSON file owns both product UI wording and UI direction. Editing these files does not require recompiling `DAP.exe`.

There are no localization fallbacks: no embedded RESX translations, no hard-coded alternate UI strings, and no automatic fallback to another language. Missing files, missing required keys, or invalid direction are explicit configuration errors.

Guide instructional content remains Guide/DB data. Product localization applies to DAP-owned interface text such as Step progress, drag-handle help, completion UI, and application messages. Developer diagnostics/logs remain technical code text.


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

The current implementation phase covers both production Learner runtimes: Web through the browser-extension adapter and Windows through Microsoft UI Automation/WPF. Both consume the same shared Core Guide/Target/Validation contracts through runtime-specific adapters. The Playwright adapter remains available as a regression/compatibility implementation while extension parity is validated.

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
- A Guide seed/factory is an initialization/reset definition only. It must never silently overwrite an existing persistent Guide during normal Learner, E2E, or visual execution.
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
- The TestCRM Web host starts successfully on `http://localhost:5200` after the WebRoot fix; the shared backend/API is on `http://localhost:5201`.
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
- Unguided runs the same persisted canonical Guide without launching DAP.exe or synchronizing learner bubbles; this baseline is locally verified PASS.
- The normal Fast + DAP path was locally re-verified after the unified-mode refactor: the persisted Guide loaded 53 Steps and the complete representative workflow ended in PASS.
- Historical note: the first verified Web From-Step implementation used Guided Fast before N. ADR-044 supersedes that behavior: current From-Step runs use Unguided bootstrap through N-1, then start DAP at N with resume context.
- `--manual-from-step <N>` and `--visual-from-step <N>` remain mutually exclusive.
- Visual frame-replacement waiting no longer requires observing the transient `#content-frame-next` Attached state. The harness waits for the stable replacement outcome/current ready Content frame, avoiding a race where the transient frame can be created/promoted before Playwright observes it.
- In full Visual mode the final Step 53 bubble is intentionally left visible briefly before the automated final action so the final Guide instruction can be observed.
- Current Web execution behaviors are therefore: full Fast, full Visual, full Manual via `--manual`, Unguided -> Manual at N, Unguided -> Visual at N, Unguided Full, and explicit Guide reset. Browser selection remains `chromium|chrome|edge` where applicable.
- Windows TestCRM was reviewed against the persisted 53-Step Web Guide. Most of the business workflow is relevant to Windows because both clients use the same server/API/business model, but Web-specific mechanics (DOM/iframe/frame URL/CSS targeting) must not be copied literally into Windows UIA tests.
- Historical parity gaps identified before the Windows canonical flow was completed have been closed for the current 53-Step baseline; Web and Windows now execute aligned business scenarios with platform-specific implementations.
- Windows validation/delete confirmations currently use WPF `MessageBox`, which is a valid platform-specific equivalent rather than the Web PS alert/confirm DOM. Dynamic Lead status behavior and conditional Selected Service UI are present and are suitable for equivalent Windows business-flow coverage.
- That historical Windows parity plan is complete: the parity gaps were closed, the Windows Unguided UI Automation E2E was implemented and stabilized, and the production Windows Runtime/target resolution/bubbles were integrated and verified through the canonical 53-Step Guided baseline.



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


## Windows Unguided canonical E2E baseline — 2026-10-02
- The Windows Unguided UI Automation E2E is implemented in `tests/DAP.TestCRM.Windows.E2E` and executes the shared canonical core scenario from `tests/DAP.TestCRM.E2E.Common` against the real WPF TestCRM client.
- Local verification reached terminal PASS: `PASS: Windows Unguided canonical Customer -> Site -> Case -> Lead core scenario completed.`
- The verified core covers Customer -> Site -> Case -> Lead navigation, Case creation/save/status FieldChange behavior, Resolution Notes enablement, required-field validation handling, Lead creation/save/status transitions, conditional Selected Service handling, Lead deletion confirmation, and return to Portal.
- WPF ComboBoxes used by the scenario expose a custom UIA ValuePattern through `AutomationComboBox`, allowing deterministic value selection without physical popup interaction.
- Case/Lead FieldChange responses are applied locally in the WPF client instead of immediately reloading the persisted record, preventing transient status changes from being reverted before Save.
- Windows E2E navigation now synchronizes on the rendered destination screen rather than assuming an async click has completed. Site breadcrumb targeting uses stable UIA identity (`AutomationId=Breadcrumb` plus the site name), and Lead creation retries safely until the form is ready.
- Modal WPF MessageBoxes are handled as real platform dialogs. Expected validation/delete dialogs are dismissed/confirmed explicitly; unexpected informational OK dialogs block further E2E actions until dismissed so the scenario cannot continue behind a modal window.
- Default Windows E2E wait timeout is 5 seconds.
- The activity-more interaction remains a Windows-specific UI area to align with the Web behavior; the canonical scenario no longer incorrectly calls validation dismissal immediately after `ShowMoreActivity`.
- Historical note: at this earlier Windows Unguided core milestone, complete 53-Step Windows parity and Guided Runtime coverage were still pending. That gap has since been closed: the canonical Windows Guide now contains and executes all 53 aligned business Steps in both Guided and Unguided paths.

- Windows/Web activity-more parity correction: Web renders `#activity-more` but defines no click handler or alert for it. The Windows-only `MessageBox` ("אין פעילויות נוספות להצגה.") was therefore removed; `ActivityMoreButton` now has the same no-alert/no-op business behavior as Web. This correction is part of the later verified 53-Step Windows baseline.

- **Windows canonical 53-step Unguided milestone: PASS (locally verified 2026-10-02).** `CanonicalCrmScenario.Run53Async` completed the full Customer -> Site -> Case -> Lead flow and terminated with `PASS: Windows Unguided canonical 53-step Customer -> Site -> Case -> Lead scenario completed.` The Windows E2E now synchronizes async sort/create/delete/navigation transitions in the test harness. Test-only row AutomationIds were removed from the Windows target application; E2E target resolution remains the responsibility of the test harness. No full-53 PASS is claimed for DAP Runtime/bubbles yet; this milestone is the Unguided Windows business-flow baseline.


## Guide persistence identity and Web E2E baseline — 2026-10-02
- DAP persistence uses numeric internal IDs for Guides and GuideSteps, with stable textual keys stored separately in `Guides.Key` and `GuideSteps.Key`.
- Existing legacy SQLite databases using TEXT primary keys are migrated automatically by `SqliteDatabaseInitializer` while preserving Guide/Step keys, ordering, bubbles, validation, context, frame data and anchors.
- The canonical TestCRM Web Guide key is `testcrm-web-canonical-workflow`; the previous `testcrm-create-case` key is migrated in place.
- The canonical Web repository seed now contains 54 Steps. The persistent DAP database remains authoritative and may still hold the previous 53-Step Guide until reset.
- Local verification on 2026-10-02 completed the full Web runtime workflow after the schema migration with terminal PASS: `PASS: representative Customer -> Site -> Case -> Lead workflow, including dynamic Lead deletion and Case deletion, completed.`
- The normal Web E2E runner is self-contained: it starts the shared TestCRM backend on port 5201 and the Web host on port 5200, waits for readiness, runs the browser/DAP scenario, then terminates only the processes it owns.
- Historical note: Windows Learner Runtime integration was the next milestone at this point. It has since been completed through the full persisted 53-Step Windows Guided baseline.

## Canonical E2E execution modes

The canonical Web test Guide is `testcrm-web-canonical-workflow`; the repository seed now defines 54 Steps, while an existing configured DAP database may still contain the previous 53-Step persisted version until reset. Both execution modes consume that same persisted Guide and the same canonical CRM business flow:

- Normal/guided mode launches `DAP.exe` and verifies production Web Runtime behavior, bubbles, validation, and Guide progression.
- Unguided omits `DAP.exe` and bubble synchronization but remains sequenced by the same 53 persisted Guide Steps. It is not an independent TestCRM QA script.

Both modes are locally verified PASS on 2026-10-02 after the Unguided Guide sequencing work. The Guide remains the source of learner sequence/targets/validation/context; synthetic E2E input values that are intentionally not encoded by generic Guide validation remain test-fixture concerns.

## Windows Learner Runtime status

Production Windows runtime code exists in `src/DAP.Runtime.Windows`. It uses UI Automation for target resolution and validation and WPF for non-activating learner bubbles. `DAP.exe` supports `--learner-windows <guide-key> --window-automation-id <id>` and consumes persisted Guide Steps from the same provider-independent persistence boundary. All 53 persisted Windows TestCRM Steps are locally verified end-to-end in Guided mode through the production runtime and in Unguided mode through the persisted-Guide action executor.

## Historical milestone — Windows persisted Guide Steps 1–12 — verified 2026-10-03

At this historical milestone, the persisted Windows Guide `testcrm-windows-canonical-workflow` had been locally verified through **Step 12** in Guided mode with the production DAP Windows Learner Runtime. Current coverage is 53/53. The terminal run passed with real UIA targets, runtime capture, and learner bubbles.

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

## Critical Guide/DB progression rule — 2026-10-03

The learner-facing flow must be reproducible from persisted Guide data plus the production Runtime alone. E2E drivers are test actors, not hidden workflow engines.

From this point forward, any condition that decides whether a learner may advance to the next Step must be represented by the Guide model and persisted through the Guide data provider. Examples include value validation, required destination/context existence, modal completion, or any other post-action condition that is part of the business flow.

The Runtime may contain generic implementation mechanics for evaluating those persisted conditions, but TestCRM-specific business knowledge must not live only in the Runtime or E2E harness. If a test discovers that progression is unsafe until another screen or target exists, treat that as a Guide-model requirement rather than merely adding a test-only wait.

Short form: **DB/Guide = what completes the Step; Runtime = how completion is detected; E2E = synthetic learner only.**

## Persisted learner-flow semantics implemented — 2026-10-03

The previously documented Guide/DB progression rule is now implemented in the shared model and both learner runtimes.

GuideStep can persist ordered post-action completion conditions. SQLite stores them independently of E2E code. Web and Windows runtimes evaluate the persisted conditions after primary validation so navigation, modal transitions, saves, deletes, and asynchronous UI changes do not advance merely because the initiating click/value event occurred.

Web runtime capture is now explicit persisted StepCaptureDefinition data and uses the shared {{step:<id>:capture}} runtime token instead of inferring URL-fragment capture ownership from downstream locator text.

Windows runtime now also evaluates persisted Step context guards. TestCRM seeds have begun moving destination/business waits from test-driver knowledge into persisted Guide semantics. The E2E driver may still contain technical synchronization needed to automate the synthetic learner, but that synchronization must not be the sole owner of learner progression rules.


## Unified manual learner execution — 2026-10-03

Web and Windows full manual TestCRM runs are now modes of the canonical platform E2E runners rather than separate PowerShell launchers.

- Web full manual: `dotnet run --project tests\DAP.TestCRM.Web.E2E\DAP.TestCRM.Web.E2E.csproj -- --manual`
- Windows full manual: `dotnet run --project tests\DAP.TestCRM.Windows.E2E\DAP.TestCRM.Windows.E2E.csproj -- --manual`
- `--manual` starts the same platform topology used by Guided execution, waits for production Step 1, then performs no synthetic learner actions.
- Manual mode is target-lifetime aware. Web ends normally only on explicit owned page-close/browser-disconnect events or DAP completion; an unexpected TestCRM Web-host exit is surfaced as an error. Windows ends when DAP completes or the owned TestCRM application exits. The runner then cleans up its remaining owned processes and returns to the shell prompt. This target-close behavior also applies during automated Guided/Unguided execution: operator closure is a clean stop, not an E2E failure.
- Manual verification on 2026-10-03 confirmed this clean-stop contract in both **Windows Guided** and **Web Guided**: deliberately closing the owned target returned the console cleanly, with owned-process cleanup and no unhandled exception/timeout.
- Web startup validates that canonical TestCRM ports 5200/5201 are free before launch and does not terminate unknown processes occupying them. HTTP readiness is coupled to the runner-owned Web-host process remaining alive; a response from a stale Web instance cannot make a failed current launch appear ready.
- TestCRM grid headers are explicitly RTL/right-aligned on both platforms. Active sorting is shown conventionally with a visible ▲/▼ direction indicator; Web also publishes `aria-sort` state.
- `--manual-from-step <N>` remains available for focused state-preserving handoff after the real prior workflow has executed.
- The removed `scripts/run-testcrm-web-learner.ps1` and `scripts/run-testcrm-windows-learner.ps1` must not be reintroduced as parallel launch paths; startup/cleanup ownership stays in the E2E runners.

The Web E2E now also regression-checks natural text commit explicitly: after typing the exact Step-1 value it verifies Step 1 is still active before blur, then sends Tab and requires advancement to Step 2 only after that commit event.

Commit semantics are hardened on both runtimes. For Web non-click validation, a blur/change report represents one commit attempt. If the committed value fails primary validation, DAP consumes that attempt and requires a new edit plus a new blur/change before reevaluating progression. Browser-side edit state is retained across reconciliation instead of being recreated every poll. Windows text validation consumes an invalid blur attempt in the same way and resets its edit-cycle baseline. Commit detection uses a target-scoped UIA property subscription for `ValuePattern.ValueProperty` and `AutomationElement.HasKeyboardFocusProperty`, with polling retained only as fallback; the Runtime also captures target focus at the UIA value-change signal to cover fast edit/blur ordering without restoring the problematic global focus listener. Canonical Windows E2E text input waits for the provider's real UIA value-change notification and then commits with a real TAB traversal. This closed the Step-8 text-commit regression and was followed by a fresh Windows Guided 53/53 PASS.

Web and Windows TestCRM runners now share the same executable-isolation rule.

- Windows Guided/Manual: TestCRM Server, TestCRM Windows, and DAP are built into `%TEMP%\DAP\E2E\Windows\<run-id>`.
- Web Guided/Manual: TestCRM Server, TestCRM Web, and DAP are built into `%TEMP%\DAP\E2E\Web\<run-id>`.
- Web Unguided uses the same Web run root for TestCRM Server/Web but does not build or launch DAP.
- Each run gets a new GUID-based directory; later runs never reuse an abandoned executable path.
- Normal cleanup plus process-exit/Ctrl+C cleanup attempts to terminate only runner-owned child processes.
- The Windows E2E project intentionally has no build-time ProjectReference to `DAP.App`; it builds DAP explicitly into its isolated run output.
- This prevents interrupted learner runs from locking normal repository `bin\Debug` outputs and blocking subsequent builds.

The unified `--manual` paths and the newest per-run isolation changes are implemented but have not yet been reported as a new full regression PASS.



### Web/Windows runner-mode symmetry

The canonical Web and Windows TestCRM runners are one public execution surface. Shared mode/switch names must have aligned user meaning across both platforms even though Web uses Playwright/DOM mechanics and Windows uses UIA/native mechanics.

The supported `DAP_E2E_MODE` vocabulary is `fast|visual` only. `demo` is removed and must not return as an alias on either platform.

Changes to `DAP_E2E_MODE`, `--guided`, `--unguided`, `--manual`, `--manual-from-step`, `--visual-from-step`, or equivalent focused-run semantics must be reviewed for both Web and Windows together. An intentional one-platform exception requires an explicit ADR instead of silent drift.


### Implemented Windows execution-mode parity
Windows now implements the shared runner-mode contract rather than merely documenting it. The canonical Windows E2E parses `DAP_E2E_MODE=fast|visual` only for full `--guided`, rejects unsupported values there, supports `--visual-from-step <N>`, and uses the existing UIA action driver for both Guided modes. Visual mode adds platform-native visible cursor movement and pacing around the same learner actions; Unguided has no Fast/Visual mode.

This change does not raise the 5-second timeout and does not introduce a separate Windows visual scenario. Focused Windows Visual From Step and full Windows Guided Visual have since been verified on the 54-Step Guide.

Runner precedence is now explicit and implemented identically on Web and Windows: `DAP_E2E_MODE` affects only full `--guided`. Manual, Unguided, Manual-From-Step, and Visual-From-Step ignore stale environment mode values. Focused runs own their transition semantics: Unguided -> Manual at N or Unguided -> Visual at N.

### From-Step bootstrap semantics — implemented

Web and Windows focused runs now treat every Step before the requested start Step as setup only. `--manual-from-step <N>` and `--visual-from-step <N>` keep DAP.exe completely off during Steps `1..N-1`, execute the real persisted business flow as Unguided bootstrap, collect any persisted runtime captures needed later, and launch DAP directly at Step N with a validated resume context.

The production learner runtimes accept initial captured values when starting from a later Step. DAP.App exposes this through `--resume-context-file` together with `--start-step`; the host rejects unknown, empty, non-capture, or non-prior resume entries. This is a general resume capability, not a TestCRM-only runtime shortcut.

New canonical meanings:
- Manual From Step: Unguided before N, Guided Manual from N.
- Visual From Step: Unguided before N, Guided Visual from N.
- Full runs retain their existing meanings.

Focused From-Step execution has since been locally verified on both platforms at Step 47: Web and Windows both completed successfully with Unguided bootstrap before Step 47, resume context transferred into DAP, and Guided Visual execution from Step 47 onward.

- Local verification after the Web/Windows Visual cursor alignment: both Web and Windows `--visual-from-step 47` completed successfully on the current 54-Step Guides. Web now uses the real Windows cursor rather than a synthetic DOM cursor; both platforms visibly move that cursor to CRM targets and to the DAP-owned `אישור` / `סיום` actions.

- Web `--manual-from-step <N>` now follows the same lifecycle principle as Windows manual runs: after handoff it automatically ends when DAP completes or the owned Web target closes. It no longer requires pressing Enter merely to let the runner exit.

## Centered targetless learner bubbles — 2026-10-04

- DAP now supports persisted informational Guide Steps that are intentionally not attached to any application element.
- Canonical definition: `Target = null`, `BubblePlacement.Center`, `StepAdvanceMode.Manual`.
- Such a Step is information-only: no Context, Validation, Capture, or CompletionConditions.
- Web and Windows both render the Step in the center of the learner surface, without target highlight or pointer.
- The centered information bubble includes the normal Step progress text and a localized explicit confirmation action (`אישור` / `OK`).
- Guide completion now reuses the same centered-bubble presentation family with completion-specific content and `סיום` / `Finish`.
- Web completion is now truly centered vertically and horizontally instead of being horizontally centered near the top of the viewport.
- The centered placement persists through the existing `BubblePlacement` SQLite field; no schema migration was required.
- The canonical Guides now intentionally include one centered informational Step near the end of the real learner flow: Step 51 warns that the Case created during the lesson is about to be deleted and requires `אישור` before progression.

- Canonical Step 51 is now a targetless centered information Step on both Web and Windows. It appears after reopening the created Case and before deletion, displays `שים לב: בשלב הבא נמחק את הפנייה שיצרת במהלך הלומדה.`, and advances only after `אישור`. The former Steps 51–53 shift to 52–54. Unguided treats this presentation-only Step as a no-op while preserving Guide order. The new seed requires explicit reset before the persistent Guide changes.

## Focused 54-Step Visual verification — 2026-10-04

Both canonical Guides were reset to the current 54-Step seed and then verified with `--visual-from-step 47`.

- Web: PASS through Step 54, including centered Step 51, real OS cursor movement to `אישור`, and real OS cursor movement to completion `סיום`.
- Windows: PASS through Step 54 with the same aligned learner flow and native cursor movement to the same DAP-owned actions.
- This verifies the newly added centered information Step and the real-cursor Visual contract cross-platform. Full Guided Fast/Visual has since passed on both platforms; a fresh complete Unguided/Manual/focused matrix remains separate.

## Windows target-attached bubble behavior during learner scrolling — 2026-10-04

Windows learner scrolling after a Step is presented must not be overridden by DAP. The initial Step-entry viewport adjustment remains allowed, but reconciliation must not repeatedly scroll the learner back to the target.

Presentation visibility is separate from Step completion semantics. If the learner scrolls a target outside a vertically scrollable viewport, the target-attached bubble is hidden rather than pinned or dragged at a viewport edge. Runtime reconciliation and persisted Guide validation continue normally. When the target becomes visible again, the bubble may be presented again from the current target geometry.

This behavior was manually verified with the current production package by starting from Step 11, scrolling the target out of view, and returning it to view. The bubble disappeared and restored as intended. The full Windows Guided Visual 54-Step production-package run also passed after this change.

### Customer production diagnostics package — 2026-10-04

The customer Production package now has an explicit diagnostics payload rather than requiring the source repository on the customer machine. `scripts/Publish-Customer-Package.ps1` publishes the framework-dependent DAP product plus prebuilt TestCRM Server, Web client, Windows client, and both E2E runners under `<Production>\Diagnostics`. Customer diagnostics therefore run without `dotnet run`, without project files, and without compiling DAP/TestCRM on the customer machine.

The packaged runners use `DAP_DIAGNOSTICS_ROOT` to resolve the prebuilt TestCRM binaries. Guided runs launch the exact sibling Production `DAP.exe`; Unguided runs intentionally omit DAP and provide the environment/application sanity baseline. Manual-From-Step and Visual-From-Step retain their canonical Unguided bootstrap before starting the same Production DAP at the requested Step.

`Diagnostics\Run-Diagnostics.ps1` exposes the six canonical modes on both Web and Windows: Fast, Visual, Manual, Unguided, ManualFromStep, and VisualFromStep. It refreshes only the dedicated canonical TestCRM Guide for the selected platform before each diagnostic run so the customer check uses the packaged 54-Step definition.

The intended customer sequence is: run Unguided first to prove the new environment and TestCRM automation path without DAP, then run Guided Fast/Visual and focused/manual modes to introduce DAP into the same known scenario.

## Windows 54-Step Fast/Visual baseline — 2026-10-05

The complete persisted Windows Guided Guide is locally verified 54/54 PASS in both Fast and Visual modes. Both modes execute the same Guide and the same synthetic learner actions. Visual is strictly a presentation layer over that action path: cursor movement and visual pacing may differ, but CRM actions, readiness conditions, validation semantics, and progression logic must not branch merely because Visual is enabled.

The Step-8 `CaseSubject` regression was closed by restoring focused-value synchronization in the Windows E2E synthetic learner. After setting a text value, the driver verifies that the new value is observable while the editor is still keyboard-focused, waits for the provider value-change notification, and then commits through real TAB focus traversal. This is E2E synchronization with observable UIA state, not a TestCRM-specific Runtime rule. Commit: `889ee17d3e34692022085760dea2496b31c0cb69`.

A clean repository run also verified that a stale `DAP_DIAGNOSTICS_ROOT` environment value cannot switch repository E2E into packaged-diagnostics mode. Packaged mode is selected only when the executing runner itself is under the diagnostics package Runners directory, preserving source/output isolation.

## Cross-platform Guided 54-Step Fast/Visual baseline — 2026-10-05

The current canonical 54-Step Guided workflow is locally verified PASS in all four full Guided mode/platform combinations: Web Fast, Web Visual, Windows Fast, and Windows Visual.

Web Fast and Visual execute the same learner-action path; mode-dependent per-character typing delay was removed so Visual remains a presentation layer around the same actions. The repository Web E2E isolated run now also copies TestCRM Web static assets into its owned temporary output, and its normal cleanup scope includes startup/navigation failures so runner-owned Web/Backend processes do not survive that exception path. Relevant commits: `401f31a582797fcb9217e521e40c0557196736c1`, `430c90176f82e636fbcea6685cd15f387b0f041d`, and `d23c6d254b7d12d057d4ea713886db8089a8ca5b`.

This establishes the current full Guided Fast/Visual cross-platform baseline. It does not imply that Unguided, Manual, or all focused From-Step variants have been re-run as a complete 54-Step matrix.



## Packaged Windows From-Step correction — 2026-10-05

Packaged Windows focused execution exposed a diagnostics-runner defect that did not exist in repository runs: the repository build path implicitly created the GUID run root, while packaged execution skipped that build and could attempt to write `resume-context.json` into a directory that did not yet exist. The Windows E2E runner now explicitly creates the run root before writing resume context. This is diagnostics/E2E orchestration only; no production Runtime, resolver, bubble, Guide, or timeout semantics changed. Fix commit: `405c1b4e577ff193be96310b8df25d6b0dc30284`.

After republishing the current customer package, Windows Production completed the full persisted 54-Step Guide successfully, and packaged `VisualFromStep` also continued successfully through Step 54.

## Production Web first-bubble startup diagnosis — 2026-10-05

A packaged Web Fast diagnostic measured 7217 ms from DAP.exe process start until the first bubble was observed. DAP's internal timing showed SQLite at 84 ms, Guide load at 144 ms, Web composition at 257 ms, `Playwright.CreateAsync()` at 6563 ms, CDP connected at 6670 ms, Web runtime start at 6673 ms, and first-bubble active-Step work at 159 ms.

This disproves the earlier working hypothesis that the multi-second delay was only an artifact of a new GUID-named repository output path. The delay also occurs from the stable `C:\DAP-Production` package. Playwright .NET 1.55.0 source confirms that `Playwright.CreateAsync()` starts its stdio driver process and initializes the Playwright connection. The current measured bottleneck is therefore Playwright/driver initialization; the 10-second CDP timeout is not a fixed startup delay and must not be increased or blamed for this measurement.

No startup optimization is accepted yet. A persistent/shared driver or another lifecycle change would be architectural work and must first be shown to be supported, robust, generic to closed customer applications, and compatible with DAP process ownership and cleanup.

## Instructor direction and AI boundary — 2026-10-05

The next architecture-validation phase uses the existing Web and Windows TestCRM systems more strictly, treating them as if they were closed customer applications. The purpose is to expose gaps in target identity, action representation, completion/transition conditions, runtime observation, and persistence before expanding the schema speculatively.

AI is explicitly a development aid in this phase. It may help analyze before/after states, diagnose difficult scenarios, identify missing general mechanisms, and accelerate implementation. The resulting production capability must not depend on AI.

The production Instructor is expected to provide its own deterministic observation workflow: capture externally observable state before an author action, observe the action and resulting state, compare the states, identify/rank candidate changes, allow the author to confirm the intended completion/transition condition, and persist an explicit Guide definition. The production Learner must then evaluate that definition and diagnose supported page/window/context changes without AI.

Schema/Core/Runtime changes should be driven by concrete scenarios that the current model cannot represent reliably. Do not enlarge the database schema merely to anticipate hypothetical cases. A capability discovered with AI assistance is complete only when the customer-side system can author/run the supported behavior without AI and without target-application source access.

## Web extension migration milestone — 2026-10-05

The persisted 54-Step `testcrm-web-canonical-workflow` was completed manually end-to-end through the production extension adapter path. This validates the integrated learner path across the canonical Customer -> Site -> Case -> Lead scenario, including server-backed refresh/reload, iframe replacement, conditional targets, validation rejection/recovery, runtime capture, context return, deletion, the cross-frame Header target, and explicit Guide completion.

The active Web adapter transport is direct:

```text
DAP.exe ↔ Named Pipe ↔ Native Host ↔ Native Messaging ↔ Extension ↔ DOM
```

The previous JSONL journals are not the active transport. The extension does not own Guide sequencing. Browser lifecycle handling now includes content readiness probing, idempotent reinjection, explicit frame-path re-resolution, and safe handling of stale content-script contexts after extension reload.

The migration used `Generic-Web-Training-Platform` only as a reference for already-proven extension messaging/lifecycle patterns. DAP does not adopt its extension-owned training engine; DAP keeps learner policy in .NET.

Current verified learner details include natural blur/change commit events, click ACK/replay semantics, validation rebinding after DOM replacement, valid-commit latching across pending server completion conditions, top-level proxy presentation for constrained frames, and stable explicit-handle proxy dragging with `grabbing` held until release.

Do not interpret this milestone as repository-wide Playwright removal. A fresh automated extension-backed browser/mode regression matrix remains separate verification work.


## Web Zero-Playwright milestone — 2026-10-05

The single active Web milestone is now **Zero Playwright**.

Definition of done:
- no active `Microsoft.Playwright` package dependency in Web Runtime or Web E2E projects;
- no active `Playwright.CreateAsync()`, `IPage`, `IFrame`, `ILocator`, or equivalent Playwright browser-control code in the Web execution path;
- `DAP.exe --learner-web` continues to use the browser-extension adapter path;
- the Web E2E/manual harness also uses the extension/browser-native path rather than a separate Playwright browser-control stack;
- the existing public Web run modes remain available with the same intent: Guided Fast Full, Guided Visual Full, Manual From Step, Unguided Full, Visual From Step, and Manual Full;
- Chrome and Edge remain supported;
- the persisted 54-Step canonical Web Guide continues to pass through the unified extension-based architecture;
- production and development/test environments do not diverge into separate Web browser architectures.

This milestone replaces smaller intermediate migration goals. Playwright may remain only as historical/reference code until removed during completion of this milestone; it is not an acceptable steady-state dependency for Web Runtime or Web test execution.


## Web architecture guardrail — Zero Playwright means one browser path

This is a hard architectural rule for all future Web work.

**Zero Playwright does not mean replacing Playwright with another browser-automation stack.**
The Web product and its Web E2E verification must use the same browser-access architecture:

```text
DAP Runtime / Web E2E driver
        ↕
Extension-facing protocol
        ↕
Native Host
        ↕
Browser Extension
        ↕
Browser DOM
```

Forbidden as an alternate Web control path:
- Microsoft.Playwright / Playwright;
- direct CDP / remote-debugging-port browser control;
- Selenium;
- Puppeteer;
- a private BrowserHarness that evaluates DOM or dispatches input through CDP;
- any second browser-control architecture used only by tests.

Allowed:
- launching Chrome/Edge as an OS process when needed;
- using the DAP browser extension and Native Messaging as the browser-control boundary;
- adding explicit E2E/test-driver commands to the same extension protocol, provided they do not change production learner semantics;
- OS-level cursor movement for Visual mode where this is part of the existing test UX.

The test harness may orchestrate servers, browser processes, DAP processes, data setup, timing, assertions, and cleanup, but browser navigation/DOM actions/element inspection must cross the DAP extension boundary rather than a separate automation technology.

Before implementing any Web change, verify it preserves this single-path rule. If a proposed solution introduces a second browser-control mechanism, stop and redesign before committing.

**Current correction:** any CDP-based BrowserHarness introduced during the Zero Playwright migration is temporary invalid work and must be removed/replaced before the milestone can be considered complete.


## Zero Playwright implementation correction — extension-native E2E

The invalid CDP-based E2E BrowserHarness has been replaced. The Web E2E runner now uses the same browser boundary as the product:

```text
Web E2E runner
  ↕ dap-web-e2e-v1 named pipe
Native Messaging Host
  ↕ Chrome/Edge Native Messaging
DAP Web Runtime extension
  ↕ extension content runtime
TestCRM DOM
```

Key rules/state:
- Chrome/Edge is launched as a normal installed browser profile; no temporary profile, remote debugging port, CDP, Playwright, Selenium, or Puppeteer is used.
- The DAP extension must already be installed/reloaded in that browser profile.
- E2E browser actions and DOM assertions are explicit test-driver commands routed through the DAP extension.
- Each run gets a unique `dap-e2e-session` token in the TestCRM tab URL.
- `DAP.exe` receives the same session through `DAP_WEB_SESSION_ID`, so learner-runtime commands and E2E actions target the same browser tab.
- The Native Host bridges both the production Runtime pipe and the E2E pipe; this is one browser-access architecture, not a second automation stack.
- Visual mode may still move the real operating-system cursor, while target lookup/action semantics remain extension-routed.
- The five-second timeout ceiling remains unchanged.

The Zero Playwright milestone is not complete until the extension-native 54-Step canonical run passes in Chrome and Edge and the required public run modes are verified.

## Hard product rule — autonomous Learner Runtime

The core product is not an automation demo and the E2E Runner is not part of learner execution.

A persisted Guide must run with only:

```text
Guide database
→ DAP Learner Runtime
→ production Web/Windows adapter
→ target application
```

The Runtime must independently determine context, resolve the target from persisted descriptors/anchors, present the bubble, observe the learner action, evaluate validation and completion conditions, capture required runtime values, and advance to the next Step. It must re-resolve after application changes and must never guess on ambiguity.

The Runner may automate a human for testing, but it must not provide target identity, validation success, completion state, transition decisions, or any other fact needed by the product. If deleting the Runner changes whether a published Guide can execute, the implementation is invalid.

The future Instructor may be developed with AI and may optionally use AI as an authoring aid, but published Guide semantics must be deterministic and persisted. Production learner execution must not require AI.

Immediate development priority: prove the canonical persisted TestCRM Guide manually with the Runner absent, using only TestCRM + DAP Learner Runtime + persisted DB + the production adapter. Automated Runner parity is secondary until this autonomous path is verified.

