# Project Context

DAP is a digital assistance platform for guiding users through existing Web and Windows applications. The installed browser extension is displayed as **GuideMe**; DAP remains the internal platform name.

## Source of truth
The current GitHub main branch defines implementation. Persisted Guide definitions in the DAP database define learner behavior. Project Markdown documents describe the current state only; Git retains history. Do not keep superseded commands, migration narratives, or old test results here.

## Product boundaries
- The learner must work with closed third-party applications without their source code, internal databases, private APIs, or AI at runtime.
- The production learner must not depend on demo-specific workflow code or test runners.
- Web uses a browser extension, Native Messaging, and the .NET runtime; Windows uses UI Automation.
- The Instructor/Editor is future work; no editor GUI is currently delivered.
- SQLite is the current storage provider behind independent data contracts.

## Runtime and data
- Product executable: `DAP.exe` (.NET 8 / WPF).
- Default database: `C:\ProgramData\DAP\Data\DAP.db`.
- Demo business database: `demos/Shared/data/sampleapp.db`, separate from the platform database.
- Example Guide IDs: `sampleapp-web-guide` and `sampleapp-windows-guide`.
- Each example Guide contains 55 persisted Steps, including a centered final summary Step.

## Production packaging
Publish with `scripts/Publish-Customer-Package.ps1 -Output C:\DAP-Production`, then register Chrome Native Messaging with `scripts/Register-WebNativeHost.ps1 -PackagePath C:\DAP-Production`. The registered host is `C:\DAP-Production\NativeHost\DAP.Runtime.Web.NativeHost.exe`. Restart Chrome after registration. Production does not depend on temporary publish folders.

## Production usage
```powershell
& "C:\DAP-Production\DAP.exe" --guide sampleapp-web-guide
& "C:\DAP-Production\DAP.exe" --guide sampleapp-windows-guide
```
`--mode manual|hybrid` is optional; Manual is the default. The Guide determines Web or Windows execution. Neither `--start-step` nor `--resume-context-file` is supported.

## Demo applications
```powershell
cd C:\yossi\ChatGpt\DAP-Platform
dotnet run --project demos\Web\Launcher\DAP.SampleApp.Web.Host.csproj
dotnet run --project demos\Windows\Launcher\DAP.SampleApp.Windows.Host.csproj
```
Run each host in its own terminal; launch the corresponding learner separately. Completing a Guide ends DAP without closing the target application.

## Documentation
- `ARCHITECTURE.md`: current implementation and boundaries.
- `APPLICATION_CONTEXT_ARCHITECTURE.md`: application discovery and context limitations.
- `DECISIONS.md`: binding design rules.
- `CURRENT_STATUS.md`: supported functionality and open gaps.
