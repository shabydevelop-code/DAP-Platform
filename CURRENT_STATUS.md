# Current Status

## Operational
- GuideMe learner: .NET 8 WPF application, launched with `DAP.exe --guide <GuideId>`.
- The persisted Guide database determines Steps, targets, anchors, context, validation, completion, and optional supported Hybrid values. Manual is the default.
- Web: Chrome GuideMe extension, Native Messaging host, named pipes, and .NET runtime. No Playwright production dependency.
- Windows: Microsoft UI Automation and WPF guidance bubbles.
- Both demo Guides contain 55 persisted Steps, including a final summary. Demo applications are independent of the production learner.
- Guide sessions in both runtimes have a single session-scoped Windows notification-area icon using the embedded GuideMe extension artwork. Its menu shows Guide identity and a confirmed **End assistance and exit** action. Bubble-level exit controls are absent. The icon is disposed when the session ends; the target application remains open.
- User verified the notification-area icon, menu and confirmed exit in Windows, and the corresponding Web behavior. Windows restores the bubble on return to the target application after focus moves away. Windows controls whether the icon is directly visible or inside notification overflow.
- Release build and customer-package publication were successful for the notification-area change. These checks do not constitute a full post-change 55-step regression.
- A Web Native Messaging host may remain running while Chrome is open even without a learner session; it can lock the deployed host files during republishing.

## Current paths and Guide IDs
- DAP database: `C:\ProgramData\DAP\Data\DAP.db`
- Demo database: `demos/Shared/data/sampleapp.db`
- Web Guide: `sampleapp-web-guide`
- Windows Guide: `sampleapp-windows-guide`
- Deployment: `C:\GuideMe`

## Build and launch
```powershell
cd C:\yossi\ChatGpt\DAP-Platform
git pull --ff-only origin main
dotnet build src\DAP.App\DAP.App.csproj -c Release
.\scripts\Publish-Customer-Package.ps1 -Output C:\GuideMe
```
Start only the relevant demo launcher in a separate terminal; each launcher manages its own server:
```powershell
dotnet run --project demos\Windows\Launcher\DAP.SampleApp.Windows.Host.csproj
dotnet run --project demos\Web\Launcher\DAP.SampleApp.Web.Host.csproj
```
Then launch the corresponding Guide separately:
```powershell
& "C:\GuideMe\DAP.exe" --guide sampleapp-windows-guide
& "C:\GuideMe\DAP.exe" --guide sampleapp-web-guide
```
Do not run the shared demo server separately alongside a launcher; both use port 5201. For a new Chrome installation, load the packaged extension and register its actual extension ID using `Register-WebNativeHost.ps1`.

## Current limitations and verification gaps
- A complete 55-step regression of both Guides after the notification-area changes is still required.
- Instructor/Editor is not implemented. A Guide currently supports one target runtime type; cross-runtime orchestration is not implemented.
- Windows multi-context switching, delayed discovery and rebinding are not implemented.
- Multi-user and Citrix operation remain unverified. The named-pipe transport is session-aware, but end-to-end isolation and concurrency require dedicated testing.
- Dynamic captures are per-run and supported for defined observable properties and target/anchor tokens. Capture timing is not fully persisted and substitution does not cover every Guide field.
- Windows UIA target-resolution performance on complex dynamic grids remains a measurement and correctness concern; do not claim universal latency improvement.
- Keyboard and screen-reader accessibility of the tray menu have not been independently verified.
