# Current Status

## Available
- .NET 8 WPF learner launched with `DAP.exe --guide <GuideId>`.
- SQLite-backed persisted Guide, Step, target, anchor, validation, bubble, and application-context definitions.
- Web learner through the **GuideMe** Chrome extension, Native Messaging bridge, and .NET runtime.
- Windows learner through Microsoft UI Automation.
- Manual execution by default and persisted-value Hybrid execution where supported.
- Enabled/disabled Steps, targetless centered informational Steps, and persisted final summary.
- Independent Web and Windows demo hosts; each example Guide currently contains 55 Steps.
- Guide completion and the bubble action **End assistance** / **סיים ליווי** stop the learner, remove its bubble, and leave the target application open.
- End-assistance behavior was verified in Web and Windows, including starting the same Guide again and stopping it again. No `DAP.exe` process remained after stopping.
- For text-input Steps, leaving a focused field (for example with TAB) triggers validation even if the value was already present and unchanged. A valid persisted value advances the Guide only after that interaction; an invalid value does not. Verified in Web and Windows.
- The Web Native Messaging host may remain running while Chrome is open without an active Guide; this is the current persistent-connection design, not evidence of a leaked learner process.
- Legacy demo database fallback and legacy Guide-key/text-ID migration execution paths have been removed.

## SampleApp Cases grid baseline (2026-10-10)
- The shared demo server now keeps at least 100 persisted Cases for SiteId=1 on startup, including after the existing demo reset/trim path. Existing records are retained; missing records are added without recreating the old temporary stress-test Guide.
- The demo cleanup cap is 100 instead of 10. This change affects SampleApp demo data only, not the DAP production runtime or Guide schema.
- GitHub change is committed; local Windows execution/build remains to be verified by the user.

## Example data
- DAP: `C:\ProgramData\DAP\Data\DAP.db`
- SampleApp: `demos/Shared/data/sampleapp.db`
- Web Guide: `sampleapp-web-guide`
- Windows Guide: `sampleapp-windows-guide`

## Commands
```powershell
cd C:\yossi\ChatGpt\DAP-Platform
git pull origin main
dotnet build src\DAP.App\DAP.App.csproj -c Release
.\scripts\Publish-Customer-Package.ps1 -Output C:\GuideMe
.\scripts\Register-WebNativeHost.ps1 -PackagePath C:\GuideMe
& "C:\GuideMe\DAP.exe" --check
```
Start the demo applications in separate terminals:
```powershell
dotnet run --project demos\Web\Launcher\DAP.SampleApp.Web.Host.csproj
dotnet run --project demos\Windows\Launcher\DAP.SampleApp.Windows.Host.csproj
```
Then run the learner separately:
```powershell
& "C:\GuideMe\DAP.exe" --guide sampleapp-web-guide
& "C:\GuideMe\DAP.exe" --guide sampleapp-windows-guide
```

## Dynamic capture verification
- The current Core engine supports per-run capture storage and token replacement in target and anchor locators; Web and Windows adapters expose capture operations.
- The locally inspected example database defines Web capture at Step 11 and reuse at Steps 12, 48 and 50. The Windows example defines capture at Step 11 and matching dynamic descendant anchors at Steps 12, 48 and 50. These are example Guide data, not hard-coded runtime behavior.
- The definitions and relevant source paths were inspected; a fresh end-to-end run proving capture against newly assigned IDs was **not** performed in this review.
- SQLite does not persist explicit capture timing. Substitution does not yet cover every Guide field. Capture values live only for the current Guide run.

## Multi-user and Citrix readiness
- Web learner and Native Messaging Host now derive the same named-pipe name using the Windows process Session ID. This separates pipe names across different Windows logon sessions. This is a code change only; no build or multi-session runtime test has yet been performed.
- A per-user, per-Windows-session transport namespace and a per-learner binding protocol are required before declaring multi-user support.
- Windows UI Automation context discovery, concurrent SQLite access, and browser-profile routing require multi-session regression.
- Citrix compatibility has not been verified; a regular Windows machine can test concurrent processes and distinct logon sessions, but does not replace a final Citrix test.

## Windows target-resolution performance
- Windows now briefly reuses resolved UIA elements for exact unanchored `automation-id` and `name` locators, rechecking their identifying property and forcing a full resolution at least once per second. This is a limited, generic optimization; anchored and dynamic grid-row targets still resolve authoritatively on each reconciliation cycle to avoid stale business-record bindings.
- Build, performance measurement, and full Web/Windows regression after this change remain pending. Further work is needed on event-driven tracking and safe invalidation for anchored/dynamic targets; do not claim the large-grid performance issue is resolved.

## Windows grid baseline fixture (demo only)
- The Windows SampleApp launcher accepts `--seed-grid-baseline` and passes it to the demo server. The server idempotently creates 100 tagged cases (`DAP-GRID-BASELINE-0001` through `0100`) under SiteId 1 in `demos/Shared/data/sampleapp.db`. Existing records and the DAP Guide database are not modified by the fixture. Tagged fixture rows are excluded from the server's existing startup trim.
- Run `dotnet run --project demos/Windows/Launcher/DAP.SampleApp.Windows.Host.csproj -- --seed-grid-baseline`; open the customer/site Cases grid and inspect UIA resolution timings while running the independent production learner. This is a baseline data fixture, not a new Guide or an automated performance assertion. Current persisted Guide targets are not automatically redirected to a tagged fixture row.
- Local build, rendering of the 100-row grid, and measured UIA performance remain unverified. Do not extrapolate these results to 1,000+ rows or claim the anchored-grid slowdown is fixed.

- Temporary anchored Windows baseline Guide utility: `dotnet run --project demos/Windows/GridBaseline/DAP.SampleApp.GridBaseline.csproj` installs `sampleapp-windows-grid-baseline-temporary` (one manual step targeting case `DAP-GRID-BASELINE-0050` within `CasesGrid`). Launch the SampleApp Windows demo with `--seed-grid-baseline`, navigate to SiteId 1 Cases, and run `C:\\GuideMe\\DAP.exe --guide sampleapp-windows-grid-baseline-temporary`. Collect the `[DAP Windows resolver timing]` and `[DAP Windows step timing]` output. The actual UIA cell name, context binding, runtime compatibility, build, and result are not yet verified; this is not a passing test until observed on Windows.
- **Mandatory temporary-test cleanup after measurements:** stop the demo and learner, run `dotnet run --project demos/Windows/GridBaseline/DAP.SampleApp.GridBaseline.csproj -- --remove` from repository root. This deletes only the named temporary Guide and tagged demo cases; subsequently remove temporary fixture code, launcher switch, and documentation from GitHub after user approval. Do not remove before completing measurements.

## Known limitations and work
- Instructor/Editor is not yet implemented. Dynamic-capture authoring assistance is a design topic, not an approved or delivered feature.
- A Guide currently runs against one target runtime type; cross-runtime orchestration is not implemented.
- Windows multi-context switching, delayed discovery, and rebinding are not implemented.
- Production Web extension and Native Messaging host no longer contain the extension-native E2E driver or its dedicated pipe. The changes require a fresh customer-package build and Web/Windows regression on the user's machine.
- End-assistance and restart checks passed in Web and Windows. A complete Web/Windows Guide regression has not been performed.

Customer package includes `ChromeExtension` (unpacked extension files) and `Register-WebNativeHost.ps1`. On a new machine, load `ChromeExtension` using Chrome's **Load unpacked**, copy the actual extension ID from `chrome://extensions`, and run the packaged registration script with `-ExtensionId <ID>`. Do not assume the development-machine extension ID. The package still requires Chrome extension loading and host registration; copying the directory alone does not install either.

## Open issue: Ending assistance when the bubble is hidden
The active Guide can continue while its bubble is hidden after the learner navigates to another screen. The current **End assistance** action is only available on the bubble, so it can become inaccessible. A user-accessible, low-friction exit path is required across Web and Windows, without adding persistent UI burden. No UX or architectural solution has been approved; do not implement a floating controller, global shortcut, or other mechanism until a design is agreed. The learner runtime must remain the owner of Guide termination, and ending assistance must not close the target application.

## Completed 100-row grid baseline (2026-10-10)
- User confirmed successful Web and Windows 100-row grid baseline checks; Web target 0050 was resolved inside the content iframe, with scrolling, sorting, and stable bubble behavior.
- The temporary GridBaseline installer and SampleApp seed flag/function were removed from the repository after the checks. Production runtime code was not modified during cleanup.
- Local database cleanup is **not verified**. The user must remove only the two temporary Guide keys (`sampleapp-web-grid-baseline-temporary`, `sampleapp-windows-grid-baseline-temporary`) and 100 SampleApp records with subjects `DAP-GRID-BASELINE-0001` through `DAP-GRID-BASELINE-0100`, if still present.
- Do not run the old `demos/Windows/GridBaseline` cleanup commands after pulling this commit; the utility has been deleted. Keep ordinary demo guides and other SampleApp data intact.

## Windows detached bubble behavior (2026-10-10)
- Fixed WindowsBubblePresenter so a manually dragged bubble no longer follows target movement/scrolling after release. Automatic placement is also suppressed during active dragging. The pointer stays hidden until the next step.
- Windows drag handle now uses a hand cursor when idle and a move cursor while dragging; the browser uses native CSS grab/grabbing cursors. Exact cursor artwork parity is not yet implemented.
- Change committed in `62e8030`. User-side Windows build and a 100-row grid scroll/drag regression check are pending.

## Windows grid-related latency investigation (2026-10-10)
- User reports delayed bubbles on screens containing grids, even for targets outside the grid; smaller grids also show delay.
- Added phase timing for completion-baseline capture, initial viewport check, and bubble presentation in WindowsGuideRuntime (`3533313`). Existing target-resolution and UIA settle timing remains available. Threshold for added phase diagnostics: 50 ms.
- No target resolution or runtime behavior was optimized yet: collect comparative logs from grid and non-grid screens before modifying resolution or UIA stability logic.
- Local build and user-side measurements are pending.
