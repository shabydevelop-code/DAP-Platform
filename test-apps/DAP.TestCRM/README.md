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
