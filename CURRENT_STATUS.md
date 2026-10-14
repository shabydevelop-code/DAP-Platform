# Current Status

## Operational
- GuideMe learner: .NET 8 WPF application, launched with `DAP.exe --guide <GuideId>`.
- The persisted Guide database determines Steps, targets, anchors, context, validation, completion, and optional supported Hybrid values. Manual is the default.
- Web: Chrome GuideMe extension, Native Messaging host, named pipes, and .NET runtime. No Playwright production dependency.
- Windows: Microsoft UI Automation and WPF guidance bubbles.
- Both demo Guides contain 55 persisted Steps, including a final summary. Demo applications are independent of the production learner.
- User-confirmed verification status: all Guides currently present in the GuideMe database have passed full validation. This is a reported completed verification, not a new automated test run in this repository session.
- Guide sessions in both runtimes have a single session-scoped Windows notification-area icon using the separate white GuideMe tray artwork. The Windows executable and Chrome extension use black lightbulb icons. A single left-click or a right-click on the tray icon opens the same menu, which shows the fixed heading **GuideMe** and an immediate **Exit** action ("יציאה" in Hebrew), without confirmation. Bubble-level exit controls are absent. The icon is disposed when the session ends; the target application remains open.
- User verified the notification-area icon, menu and confirmed exit in Windows, and the corresponding Web behavior. Windows restores the bubble on return to the target application after focus moves away. Windows controls whether the icon is directly visible or inside notification overflow.
- The Windows-to-Web launch sequence was manually verified: the Windows Guide opened, and the subsequently launched Web Guide displayed its bubble and advanced. During a working Web session, `DAP.exe` and `GuideMe.NativeHost.exe` were both observed in Windows session 25. This is a focused smoke test, not a full 55-step regression.
- Web startup has also intermittently failed to connect or repeatedly reported a hidden first target. The cause has not been established; successful subsequent runs do not establish a permanent fix.
- A Web Native Messaging host may remain running while Chrome is open even without a learner session; it can lock the deployed host files during republishing.

## Localization and build
- Learner-facing UI text is supplied by the shared Hebrew/English catalogs in `src/DAP.App/Localization`. The tray exit label, Windows learner UI and Web bubble labels use localized text; the fixed product name **GuideMe** is not translated.
- `scripts/check-localization.py` runs automatically before `DAP.App` compilation. It checks translation-key parity and common direct UI literals in selected learner UI entry points, with file-and-line diagnostics. It is intentionally lightweight, not exhaustive; Python must be available on the build machine.
- The updated localization check and Windows x64 framework-dependent publish completed successfully on the development machine.

## Current paths and Guide IDs
- GuideMe database: `C:\ProgramData\GuideMe\Data\GuideMe.db`
- Demo database: `demos/Shared/data/sampleapp.db`
- Web Guide: `sampleapp-web-guide`
- Windows Guide: `sampleapp-windows-guide`
- Deployment: `C:\GuideMe`

## Build and launch
The packaged browser Native Messaging process is `GuideMe.NativeHost.exe` (internal project remains `DAP.Runtime.Web.NativeHost`). The Chrome extension description is **GuideMe – Interactive Software Guidance**. Existing Chrome Native Messaging registration must be refreshed after deploying the renamed executable.

The complete customer package is published with `scripts/Publish-Customer-Package.ps1 -Output C:\GuideMe`, including the .NET learner, Native Messaging host, Chrome extension files and icons, and registration script. For an existing deployment, `scripts/Publish-GuideMe.ps1` publishes the learner and synchronizes the three Chrome icon files to its existing `ChromeExtension` directory; it does not rebuild or register the Native Messaging host. Its publish and icon-hash verification passed on the current Windows deployment.

```powershell
cd C:\DAP-Platform
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
- All currently persisted Guides have passed full validation according to the user's latest confirmation. This does not establish that future changes or new Guides have been validated. Historical intermittent Web startup/first-target behavior has not been independently root-caused.
- Instructor/Editor is not implemented. A Guide currently supports one target runtime type; cross-runtime orchestration is not implemented.
- Windows multi-context switching, delayed discovery and rebinding are not implemented.
- Multi-user and Citrix operation remain unverified. The named-pipe transport is session-aware, but end-to-end isolation and concurrency require dedicated testing.
- Dynamic captures are per-run and supported for defined observable properties and target/anchor tokens. Capture timing is not fully persisted and substitution does not cover every Guide field.
- Windows UIA target-resolution performance on complex dynamic grids remains a measurement and correctness concern; do not claim universal latency improvement.
- Keyboard and screen-reader accessibility of the tray menu have not been independently verified.
