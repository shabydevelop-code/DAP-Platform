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
- The Web Native Messaging host may remain running while Chrome is open without an active Guide; this is the current persistent-connection design, not evidence of a leaked learner process.
- Legacy demo database fallback and legacy Guide-key/text-ID migration execution paths have been removed.

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

## Known limitations and work
- Instructor/Editor is not yet implemented.
- A Guide currently runs against one target runtime type; cross-runtime orchestration is not implemented.
- Windows multi-context switching, delayed discovery, and rebinding are not implemented.
- Web Native Host and extension still contain test-driver transport hooks; product/test isolation is not fully complete.
- A production package and test-driver separation audit remains necessary.
- End-assistance and restart checks passed in Web and Windows. A complete Web/Windows Guide regression has not been performed.
