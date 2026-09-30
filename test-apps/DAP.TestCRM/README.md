# DAP.TestCRM

Permanent server-backed target application for DAP integration and end-to-end testing.

## Business model

- Customer has many Sites.
- Site has many Cases.
- Site has many Leads.
- Existing records can be opened and new Site/Case/Lead records can be created.

## Purpose

This application deliberately contains behaviors a production DAP runtime must handle:

- Server-backed data.
- Asynchronous requests.
- Dynamic DOM replacement after a server response.
- Tab/content replacement.
- Record navigation.
- Grids with selectable records.
- Create/edit/save flows.

It is a test target, not part of the DAP product runtime.

## Run

```powershell
cd test-apps\DAP.TestCRM
dotnet run
```

Use the localhost URL printed by ASP.NET Core.
