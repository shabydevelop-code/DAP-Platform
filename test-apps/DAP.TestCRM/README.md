# DAP.TestCRM

Permanent server-backed CRM target application for DAP integration, end-to-end regression testing, manual learner walkthroughs, and realistic customer-facing demonstrations.

It is a test target for DAP. It is not part of the DAP product runtime.

## Business model

- Customer has many Sites.
- Site has many Cases.
- Site has many Leads.
- Existing records can be opened and edited.
- New Customers, Sites, Cases, and Leads can be created through server-backed flows.
- Sites, Cases, and Leads support real persisted updates; Cases and Leads also participate in the representative create/delete workflow.

The representative DAP flow exercises the business path:

`Customer -> Site -> Case -> Lead`

while preserving and later re-resolving stable business context.

## Internal structure

DAP.TestCRM is one shared test system with separate server, Web UI, and shared data ownership:

```text
test-apps/DAP.TestCRM/
  Server/
    DAP.TestCRM.Server.csproj
    Program.cs
  Web/
    wwwroot/
  data/
    testcrm.db
```

The server project lives under `Server/DAP.TestCRM.Server.csproj`. The Web UI and Windows UI are clients of the shared CRM server/API; neither UI owns the business database.

## Persistence

DAP.TestCRM uses a real SQLite database rather than browser-only or in-memory state.

The shared CRM database is:

```text
test-apps/DAP.TestCRM/data/testcrm.db
```

For backward compatibility, if the new shared database does not yet exist but the legacy `test-apps/DAP.TestCRM/testcrm.db` exists, startup moves that database into `data` before initialization. This preserves existing local CRM business data after pulling the refactor.

On startup the server:

1. Opens `testcrm.db`.
2. Creates the required tables if they do not exist.
3. Applies the small compatibility column additions currently required by Cases and Leads.
4. Seeds the initial sample Customers, Sites, Cases, and Leads only when the Customers table is empty.

Restarting the browser, DAP, or the CRM server does not normally reset saved business data. Once the database contains the seed/business records, startup does not overwrite them.

The current persisted entities are:

- `Customers`
- `Sites`
- `Cases`
- `Leads`

## Real server-backed save behavior

Browser screens read and write data through HTTP API requests.

Successful business saves execute real SQLite statements:

- Create operations use `INSERT`.
- Edit/save operations use `UPDATE`.
- Delete operations use `DELETE`.
- Reads and grids use `SELECT`.

For example, saving an existing Case calls `PUT /api/cases/{id}`, performs server validation, and then updates the persisted Case fields in SQLite.

A FieldChange is deliberately different from Save. FieldChange endpoints model a PeopleSoft-style server round trip and return dependent UI state, but they do not by themselves persist the edited record. Persistence occurs through the corresponding successful create/update request.

Server validation can therefore reject a save while the browser preserves the learner's working values. Only a successful save changes the persisted record.

## HTTP API

The ASP.NET Core backend exposes business APIs for:

- Customer search/read/create.
- Site read/list/create/update/delete.
- Case read/list/create/update/delete and Status FieldChange.
- Lead read/list/create/update/delete and Status FieldChange.

Grid sorting is performed by the server using allow-listed sort fields and SQL `ORDER BY`; clicking a supported grid header sends a new request with `sort` and `dir`.

## Run

From the repository root:

```powershell
cd C:\yossi\ChatGpt\DAP-Platform
dotnet run --project test-apps\DAP.TestCRM\Server\DAP.TestCRM.Server.csproj
```

The shared TestCRM backend/API started by the command above listens on:

```text
http://localhost:5201
```

The Web UI is a separate host at `http://localhost:5200` when `DAP.TestCRM.Web` is running.

For canonical DAP regression and learner runs, do **not** pre-start the backend or Web host in separate terminals. The platform E2E runner owns startup, readiness, isolated per-run outputs, and cleanup for the processes it launches.

Web Guided:

```powershell
cd C:\yossi\ChatGpt\DAP-Platform
dotnet run --project tests\DAP.TestCRM.Web.E2E\DAP.TestCRM.Web.E2E.csproj -- --guided
```

Web Unguided:

```powershell
cd C:\yossi\ChatGpt\DAP-Platform
dotnet run --project tests\DAP.TestCRM.Web.E2E\DAP.TestCRM.Web.E2E.csproj -- --unguided
```

Web full manual learner run:

```powershell
cd C:\yossi\ChatGpt\DAP-Platform
dotnet run --project tests\DAP.TestCRM.Web.E2E\DAP.TestCRM.Web.E2E.csproj -- --manual
```

Standalone Server/Web startup is still valid when developing the TestCRM clients themselves, but it is not the canonical DAP E2E topology.

## Purpose and realism rule

DAP.TestCRM is both a permanent runtime regression target and a credible CRM demo environment. Technical edge cases must be represented through plausible CRM behavior rather than artificial test controls.

The reference workflow includes customer search, Site navigation, server-backed grids, sorting, opening business records, FieldChange, dependent fields, server validation, save/update, navigation between business contexts, creation and deletion.

Do not add test-only buttons or obviously artificial screens merely to exercise DAP. New runtime cases should receive a plausible CRM business scenario whenever practical so the same flow remains useful for regression testing, manual testing, live demonstrations, and recorded demos.

## Server round-trip feedback

Server activity follows one consistent UX rule:

- Keep the current CRM content visible whenever possible.
- Show a compact `מעבד...` spinner while the server request is active.
- Do not replace the Content area with a generic loading placeholder during iframe reload/reconstruction.
- Temporarily block duplicate interaction when necessary without visually hiding the business screen.
- After context restoration, show transient success feedback for successful saves and the server-returned modal/validation state for rejected operations.

This applies to server-backed search, sorting, FieldChange, save/update, delete, and validation flows.

Outside E2E fast mode the server currently includes a short artificial response delay so these round trips remain visible during manual/demo execution. Fast E2E bypasses that delay through its request mode.

## PeopleSoft-style runtime coverage

The permanent CRM currently provides business-shaped scenarios for DAP target/context resolution:

- Case Status FieldChange can remove and restore the conditional Close Reason target.
- Treatment Notes is disabled while a Case is Open and becomes enabled after the relevant server-backed status transition.
- Cases grids contain repeated Open actions associated with different business records.
- Case-grid Open actions receive transient generated DOM IDs on render, so stable DAP resolution must use business identity/context rather than those IDs.
- Server-side sorting rebuilds and reorders grids, requiring target re-resolution.
- Case history makes the record screen vertically scrollable and supplies legitimate off-screen targets.
- Conditional fields and validation summaries change layout and move downstream targets.
- Opening a Case from the grid can replace the Content iframe element, requiring frame and target re-resolution.
- Case FieldChange can reload the Content document while preserving the same logical Case route/context; document reload alone is not treated as a business-context transition.
- The representative workflow leaves the created Case context for Leads and later returns to the exact Case by stable business identity.
- Server validation inserts a validation summary, marks rejected fields, preserves working values, and presents the returned error modal.
- CRM tab switching and cross-frame navigation exercise context preservation across Header and Content frames.
- Conditional target disappearance/reappearance, layout shifts, consecutive server updates, and explicit business-context switching are exercised through the representative scenario.
- Lead creation includes server-backed FieldChange and conditional validation.
- The representative workflow includes dynamic Lead deletion and Case deletion.

## Representative DAP regression scenario

The current repository DAP TestCRM Guide seeds define 54 Steps and are exercised against ten representative PeopleSoft-style scenarios. Existing persisted Guides may remain at the previous 53-Step version until explicitly reset:

1. Case status FieldChange + Content iframe replacement.
2. Case validation failure + preservation of unsaved values.
3. Grid rerender/reorder + target re-resolution.
4. Full page reload + business context preservation.
5. CRM tab switching + business context preservation.
6. Conditional target disappearance/reappearance + re-resolution.
7. Cross-frame navigation from Header to Content.
8. Layout shift + target re-resolution.
9. Consecutive server updates + final-state re-resolution.
10. Business context switch + target isolation.

The same representative run covers the complete Customer -> Site -> Case -> Lead workflow, including Lead creation, FieldChange/conditional validation, dynamic Lead deletion, and Case deletion.

These scenarios must continue to exercise user-visible application behavior and generic DAP runtime mechanisms. TestCRM-specific workarounds must not be introduced merely to make a DAP test pass.

## Current boundary

DAP.TestCRM is intentionally more realistic than a static test page, but it remains a focused test/demo CRM rather than a production CRM product. Its job is to provide deterministic, persistent, server-backed business behavior against which the DAP runtime can be validated.

Duplicate-event/idempotency behavior is not claimed as a completed scenario yet; it requires the corresponding production runtime event model rather than an artificial CRM-only control.


## Windows client

The first real Windows TestCRM client now lives under `Windows/` as a WPF application.

- It is a second client of the shared TestCRM HTTP API at `http://localhost:5201`.
- It never opens `data/testcrm.db` directly; the TestCRM server remains the sole owner of SQLite access.
- Web and Windows therefore operate on the same Customers, Sites, Cases, and Leads.
- The Windows client supports the canonical Customer -> Site -> Case -> Lead workflow used by the aligned Guide scenarios. The current seed contains 54 Steps, including a centered informational pause before final Case deletion, plus create/update/delete and server-backed FieldChange/validation behavior.
- Important WPF controls have explicit `AutomationProperties.AutomationId` values so the application can later serve as a realistic UIA target for DAP Windows Runtime.

Run the server first:

```powershell
dotnet run --project test-apps\DAP.TestCRM\Server\DAP.TestCRM.Server.csproj
```

Then, from a second terminal, run Windows:

```powershell
dotnet run --project test-apps\DAP.TestCRM\Windows\DAP.TestCRM.Windows.csproj
```


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

## Windows learner-scroll regression coverage — 2026-10-04

The Windows canonical workflow is also used to verify target-attached bubble behavior in a real scrollable CRM screen. After a Step is presented, manually scrolling its target outside the visible scroll viewport must hide the bubble rather than leave it pinned or dragged at the viewport edge. Returning the target to view must allow the bubble to reappear beside the live target.

This was manually verified with `--manual-from-step 11` against the framework-dependent published DAP package at `C:\DAP-Production`. The current full Windows Guided Visual run against the same published package also completed all 54 Steps and exited cleanly.


## Web extension learner milestone — 2026-10-05

The persisted 54-Step Web Guide has been completed manually end-to-end through the production browser-extension adapter path:

```powershell
cd C:\yossi\ChatGpt\DAP-Platform
dotnet run --project .\src\DAP.App\DAP.App.csproj -- --learner-web testcrm-web-canonical-workflow
```

This run exercised the same canonical Customer -> Site -> Case -> Lead business flow, including server-backed FieldChange, iframe/document replacement, conditional fields, validation failure/recovery, runtime capture, business-context return, dynamic Lead deletion, Case deletion, cross-frame Header interaction, and final Guide completion.

The cross-frame Header Step uses `#portal-header` inside `iframe[name='dap-header']`. Its target remains the whole header element, not the inner `DAP Test CRM` text node. When the child frame cannot contain the bubble, presentation may be promoted to the top-level page while click validation remains bound to the original header target.

Focused Step 54 execution was also used to verify promoted-bubble dragging: manual position remains authoritative during reconciliation and the cursor stays in the active `grabbing` state until release.

This verification covers the production learner path. It does not replace a future automated extension-backed cross-browser/mode matrix.
