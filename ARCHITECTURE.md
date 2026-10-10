# Architecture

## Product components
- `src/DAP.App`: .NET 8 WPF host, CLI, session lifecycle.
- `src/DAP.Core`: Guide, Step, target, anchor, validation, and context models.
- `src/DAP.Data`: storage abstractions.
- `src/DAP.Data.Sqlite`: SQLite implementation and current schema initialization.
- `src/DAP.Runtime.Web`: Web Guide execution.
- `src/DAP.Runtime.Web.Extension`: Chrome adapter displayed as **GuideMe**.
- `src/DAP.Runtime.Web.NativeHost`: Native Messaging / named-pipe bridge.
- `src/DAP.Runtime.Windows`: Windows UI Automation adapter.

## Execution
`DAP.exe --guide <GuideId>` loads enabled persisted Steps and application contexts. The enabled target types determine which runtime starts; a single Guide currently requires one target runtime type. The runtime owns target resolution, observation, validation, completion, and advancement.

A Step may contain a target descriptor, multiple anchors, application context, validation and completion rules, capture/materialization, bubble definition, advance mode, enabled flag, and optional Hybrid automation value. Ambiguous or missing targets must not be guessed. Disabled Steps are skipped without renumbering. Targetless centered Steps are supported.

## Web
```text
DAP.exe → .NET Web Runtime → ExtensionWebBrowserAdapter
        → named pipe → Native Messaging host
        → GuideMe Chrome extension → content script → target DOM
```
For text-input validation Steps, the extension reports a blur as a commit attempt even when no input/change event occurred. The .NET runtime still checks persisted validation and completion conditions before advancing. The extension observes and interacts with the page; Guide sequencing stays in .NET. Frames, DOM replacement, target re-resolution, and browser events are handled by the Web adapter. Production Web does not use Playwright.

## Windows
The Windows runtime resolves targets with UI Automation, presents bubbles, observes actions, and applies persisted validation rules. For Edit targets, a previously focused field losing focus counts as a commit attempt even when its value is unchanged; validation still controls progression. A single persisted Windows application context can identify an existing top-level window. Windows and Web share runtime-neutral Guide semantics.

## Data and isolation
Default DAP SQLite database: `C:\ProgramData\DAP\Data\DAP.db`. Demo business data is separate at `demos/Shared/data/sampleapp.db`. Neither the product runtime nor Guide progression may rely on demo application internals. Test harnesses are separate consumers of production functionality, not product dependencies.

## Session lifecycle
Guide completion or the bubble's **End assistance** / **סיים ליווי** action dismisses the learner UI and ends the DAP process without closing the target application. A Guide can be launched again after stopping. The Web Native Messaging host is browser-owned infrastructure: the extension establishes a persistent native connection on startup/installation and service-worker initialization, so the host may outlive individual learner sessions and remain running while no Guide is active. This is accepted current behavior; on-demand connection lifecycle is not a prerequisite for correct Guide operation. Publishing to the same output directory requires stopping processes that use the deployed files. Production still contains some test-driver transport hooks; their isolation is an outstanding engineering task, not an intended product dependency.

## Delivery
The Windows x64 framework-dependent package includes the learner and a complete Native Messaging host under `C:\GuideMe\NativeHost`. Chrome registration points to the packaged executable, not a build or temporary directory. Restart Chrome after registration.

```powershell
dotnet build src\DAP.App\DAP.App.csproj -c Release
.\scripts\Publish-Customer-Package.ps1 -Output C:\GuideMe
.\scripts\Register-WebNativeHost.ps1 -PackagePath C:\GuideMe
& "C:\GuideMe\DAP.exe" --check
```
