# Project Context

DAP is a digital assistance platform for guiding users through existing Web and Windows applications. The user-facing product and installed browser extension are named **GuideMe**; DAP remains the internal name of executable and code components.

## Source of truth
The current GitHub main branch defines implementation. Persisted Guide definitions in the DAP database define learner behavior. Project Markdown documents describe the current state only; Git retains history. Do not keep superseded commands, migration narratives, or old test results here.

## Product boundaries
- The learner must work with closed third-party applications without their source code, internal databases, private APIs, or AI at runtime.
- The production learner must not depend on demo-specific workflow code or test runners. The Web extension and Native Messaging host do not expose the former extension-native E2E driver or dedicated test pipe.
- Web uses a browser extension, Native Messaging, and the .NET runtime; Windows uses UI Automation.
- The Instructor/Editor is future work; no editor GUI is currently delivered.
- SQLite is the current storage provider behind independent data contracts.

## Runtime and data
- Product executable: `DAP.exe` (.NET 8 / WPF).
- Default database: `C:\ProgramData\GuideMe\Data\GuideMe.db`.
- Demo business database: `demos/Shared/data/sampleapp.db`, separate from the platform database.
- Example Guide IDs: `sampleapp-web-guide` and `sampleapp-windows-guide`.
- Each example Guide contains 55 persisted Steps, including a centered final summary Step.
- Web and Windows text-input Steps validate an already-present value after the user focuses and leaves the field, without requiring a value change; an invalid value cannot advance the Guide.

## Production packaging
For a complete customer package, publish with `scripts/Publish-Customer-Package.ps1 -Output C:\GuideMe` (including the learner, Native Messaging host, extension and icon assets), then register Chrome Native Messaging with `scripts/Register-WebNativeHost.ps1 -PackagePath C:\GuideMe`. The registered host is `C:\GuideMe\NativeHost\DAP.Runtime.Web.NativeHost.exe`. Restart Chrome after registration. For an existing `C:\GuideMe` deployment, `scripts/Publish-GuideMe.ps1` republishes the learner and synchronizes and verifies the three Chrome extension icon files. It does not replace the complete customer package or register the Native Messaging host. The Windows executable and Chrome extension use black lightbulb artwork; the session notification-area icon remains white. Production does not depend on temporary publish folders.

## Production usage
```powershell
& "C:\GuideMe\DAP.exe" --guide sampleapp-web-guide
& "C:\GuideMe\DAP.exe" --guide sampleapp-windows-guide
```
`--mode manual|hybrid` is optional; Manual is the default. The Guide determines Web or Windows execution. Neither `--start-step` nor `--resume-context-file` is supported.

## Demo applications
```powershell
cd C:\yossi\ChatGpt\DAP-Platform
dotnet run --project demos\Web\Launcher\DAP.SampleApp.Web.Host.csproj
dotnet run --project demos\Windows\Launcher\DAP.SampleApp.Windows.Host.csproj
```
Run each host in its own terminal; launch the corresponding learner separately. Completing a Guide or confirming **End assistance and exit** in the GuideMe Windows notification-area menu (opened with either a single left-click or a right-click) ends DAP without closing the target application. The same embedded GuideMe icon is used for Web and Windows Guide sessions; no exit control appears in guidance bubbles. The same Guide may be launched again. The Web Native Messaging host may remain active while Chrome is open because the extension maintains a persistent connection; it is independent of an active learner session. A focused Windows-then-Web launch check succeeded, but intermittent Web startup/target-visibility failures are not yet diagnosed and a complete 55-step regression is pending.

## Documentation
- `ARCHITECTURE.md`: current implementation and boundaries.
- `APPLICATION_CONTEXT_ARCHITECTURE.md`: application discovery and context limitations.
- `DECISIONS.md`: binding design rules.
- `CURRENT_STATUS.md`: supported functionality and open gaps.
