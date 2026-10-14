# Reference Guide — target inventory and candidate step plan

Status: **code-grounded candidates, not a persisted Guide and not a completed runtime test**. Source reviewed on main, 2026-10-14. Existing canonical Guides and their DB records must remain untouched.

## Source inventory

### Web SampleApp
- `demos/Web/App/wwwroot/index.html`: `iframe#header-frame` (`/header.html`) and `iframe#content-frame` (`/content.html`). The content frame is replaced by `replaceContentFrame`, and its name/id are restored after replacement.
- `demos/Web/App/wwwroot/header.html`: `#portal-header` with role `link`; click returns to portal.
- `demos/Web/App/wwwroot/app.js`: dynamic screens under `main#app`; search form `#customer-search` with `input[name=name]`, `input[name=phone]`, `input[name=email]`; `#search-results` populated from API; sort buttons `[data-sort]`; customer new form `#customer-form`; site editor `#site-edit`; dynamically generated `button.grid-open[data-business-id]` in cases grid, with a render token embedded in the button ID; tab buttons use `[data-go]` values; server refresh and dependent status field changes exist.
- Dynamic row IDs and record IDs must be observed at runtime; never hardcode them from source.

### Windows SampleApp
- `demos/Windows/App/MainWindow.xaml`: main window `AutomationId=TestCrmMainWindow`, content host `ScreenHost`, header button `PortalHeader`, status `StatusText`.
- `demos/Windows/App/MainWindow.xaml.cs`: explicit IDs `CustomerNameSearch`, `CustomerPhoneSearch`, `CustomerEmailSearch`, `SearchCustomersButton`, `NewCustomerButton`, `CustomersGrid`; `SiteDetailsTab`, `CasesTab`, `LeadsTab`, `CasesGrid`, `NewCaseButton`; `CaseStatus`, `CaseSubject`, `CaseDescription`, `CaseCloseReason`, `SaveCaseButton`; `LeadsGrid`, `LeadStatus`, `SaveLeadButton`.
- `GridFor` assigns grid accessible names; rows/headers are UIA-provider-generated and require a live UIA inspection before defining row/column-header anchors.

## Candidate step sequence (not yet ready for DB insertion)

| Group | Candidate action | Concrete code-backed target | Coverage goal | Precondition / live verification |
|---|---|---|---|---|
| W1 | Open portal or explain workflow | Header `#portal-header` | iframe context, role locator, information-only | Confirm extension resolves header frame and role matching |
| W2 | Search for customer | `#customer-search input[name=name]` | CSS locator; context/ancestor anchor | Verify exact customer record and search workflow |
| W3 | Submit search | `#customer-search button.primary` | CSS descendant/ancestor and validation | Verify rendered button, commit event |
| W4 | Sort results | `#search-results [data-sort=name]` (candidate) | sibling/nearby or multi-anchor, dynamic rerender | Verify matching DOM after data load; do not assume selector exists before search |
| W5 | Open customer/site/cases | dynamic `[data-go]` buttons | navigation, dynamic locator, frame replacement | Discover actual customer/site IDs and transition |
| W6 | Open a specific case | `button.grid-open[data-business-id]` | two anchors, ambiguous vs unique, changing button ID | Confirm row DOM and business ID, avoid hardcoding dynamic ID |
| W7 | Change status / save | form controls from `casePage` | field change, completion condition, capture timing | Confirm exact rendered form controls and API behavior |
| X1 | Switch to Windows context | Existing mixed smoke pattern | cross-runtime transition | Inspect existing persisted smoke Guide; verify launch route |
| N1 | Search for customer | `CustomerNameSearch` | automation-id and Self anchor | Live UIA name/control-type inspection |
| N2 | Submit search | `SearchCustomersButton` | name and control-type locator alternatives | Verify UIA names and uniqueness |
| N3 | Select a row | `CustomersGrid` plus actual row child | Context/Ancestor + Descendant anchors | Live UIA row ownership and selection |
| N4 | Navigate to cases | `CasesTab` | Sibling/Nearby anchor where observable | Verify actual UIA sibling tree |
| N5 | Identify case in grid | `CasesGrid` plus row and column header | ColumnHeader and multiple anchors | Live UIA tree essential; column-header relation has specialized resolver |
| N6 | Edit case status | `CaseStatus` | validation, field-change, capture | Verify enabled controls and actual action |
| N7 | Save case | `SaveCaseButton` | completion condition, re-resolution | Verify persisted state and cleanup |
| X2 | Return to Web | Existing mixed smoke pattern | context switching | Verify Web session continuity |

The candidate steps are a **coverage map**, not a runnable script: adding them now would require guessing dynamic record IDs, UIA hierarchy, exact Web selectors and validation semantics. The final reference may have more steps to navigate the actual app.

## Immediate executable read-only baseline

Run on the Windows machine, from the repository root:

```powershell
python .\scripts\Analyze-Instructor-Coverage.py
python .\scripts\Export-Guide-For-Instructor.py --guide sampleapp-mixed-smoke-guide --output mixed-guide-export.json
```

These commands read the real DB without changing Guides. The first writes `instructor-capability-coverage.json` with current Guide distributions and exports. Review the output before authoring new reference steps. The second exports the verified mixed smoke Guide. Both generated JSON files are local artifacts, not automatically committed.

## Before creating the reference Guide

1. Compare exported canonical and mixed Guide contexts, capture definitions, anchors, and validation kinds against the code-grounded candidate targets above.
2. Run live DOM/UIA inspection for the selected app screens and record candidate counts for each target and anchor. Source is only an aid for target discovery; do not make production runtime depend on app source.
3. Produce an exact per-step manifest: stable key, runtime, context, locator, anchor list, frame path, bubble, validation, completion conditions, capture and expected observable outcome.
4. Create **new** DB Guide key(s) only, with backup and rollback path. Never alter `sampleapp-web-guide`, `sampleapp-windows-guide`, or `sampleapp-mixed-smoke-guide`.
5. Execute and freeze a successful reference Guide, then reproduce with Instructor and compare persisted structure and live behavior.
