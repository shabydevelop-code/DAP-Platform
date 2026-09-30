# DAP.TestCRM

Permanent server-backed target application for DAP integration and end-to-end testing.

## Business model

- Customer has many Sites.
- Site has many Cases.
- Site has many Leads.
- Existing records can be opened and new Site/Case/Lead records can be created.

## Persistence and server behavior

- Data is persisted in SQLite at `testcrm.db`.
- The database is created and seeded automatically on first run.
- Browser screens read and write data through HTTP API requests.
- Grid sorting is performed by the server using allow-listed sort fields and `ORDER BY`; clicking a grid header sends a new request with `sort` and `dir`.
- Breadcrumbs are available throughout the application and always provide a route back to the customer portal.

## Purpose

This application deliberately contains behaviors a production DAP runtime must handle:

- Persistent server-backed data.
- Asynchronous requests.
- Dynamic DOM replacement after a server response.
- Tab/content replacement.
- Record navigation.
- Server-sorted grids with selectable records.
- Create/edit/save flows.

It is a test target, not part of the DAP product runtime.

## Run

```powershell
cd test-apps\DAP.TestCRM
dotnet run
```

Open `http://localhost:5200`.


## Realistic DAP demo and runtime scenarios

DAP.TestCRM serves both as a permanent runtime test target and a credible customer-facing demo environment. Technical edge cases must be represented through realistic CRM behavior rather than artificial test controls.

Reference flow: search for a customer, open a site, enter a server-backed grid, sort it, open a business record, trigger a server FieldChange, handle dependent fields and validation, save, return to the grid, and continue to another record.

The CRM should progressively cover iframe/document replacement, server round trips, transient DOM replacement, repeated grid targets, conditional fields, targets that appear or disappear, scrolling to off-screen targets, modal overlays, server validation, target movement/re-sizing, and navigation between business contexts.

Do not add test-only buttons or obviously artificial screens merely to exercise DAP. New runtime test cases should receive a plausible CRM business scenario whenever practical, so the same flows can be reused for regression testing, live demonstrations, and recorded customer-facing videos.


## Server round-trip feedback

Server activity uses one consistent system-wide behavior:

- Keep the current CRM content visible whenever possible.
- Show a compact spinner with "מעבד..." while the server request is active.
- Do not show a generic "טוען..." placeholder during Content iframe reload/reconstruction.
- Temporarily block duplicate interaction when necessary without visually hiding the business screen.
- After context restoration, show transient success feedback for successful saves and the server-returned modal for validation/errors.

The rule applies to all server-backed CRM actions, including search, sorting, FieldChange, save/update, delete, and validation.


## PeopleSoft Web runtime scenario coverage

The permanent CRM flow now includes concrete business-shaped scenarios for DAP target resolution and bubble behavior:

- Case Status FieldChange can remove and restore the conditional Close Reason target.
- Treatment Notes is disabled while a Case is Open and becomes enabled after a server-backed status transition.
- The Cases grid contains repeated identical Open actions, each bound to a different business record.
- Case history makes the record screen vertically scrollable and provides legitimate off-screen targets.
- Conditional Close Reason and validation summaries change layout and move downstream targets.
- Opening a Case from the grid replaces the Content iframe element, requiring frame and target re-resolution.
- Server validation inserts a validation summary, marks rejected fields, preserves working values, and then presents the server error modal.

These are permanent regression/demo scenarios. They must remain realistic CRM behavior rather than test-only controls.
