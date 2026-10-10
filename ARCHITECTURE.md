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

## Captured values and dynamic targets
A persisted `StepCaptureDefinition` identifies a runtime-observable source (locator, property and optional extraction pattern). `GuideRunPlan` records the captured string by Step ID for the current run. Subsequent target and anchor locator values may contain `{{step:<step-id>:capture}}`; materialization replaces the token with the observed value and rejects missing captures rather than choosing a fallback target. This supports linking a newly created business record to later steps without accessing the target application's source or business database.

Capture is currently limited to supported adapter properties and capture-token substitution in target/anchor locators. `StepCaptureTiming` exists in Core but the SQLite `StepCaptures` repository does not persist its timing field, so platform defaults apply. Web defaults to before-action capture; Windows supports during-step/after-action capture. These defaults are not interchangeable guarantees. Captures are per-run, not durable business records. The Instructor's future authoring workflow for suggesting and confirming dynamic captures is not implemented or approved as a product design.

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
Guide completion or confirmed exit from the GuideMe notification-area menu dismisses learner UI and ends the DAP process without closing the target application. A single session-scoped tray icon is created by the WPF host for either Web or Windows; it uses embedded extension artwork and is removed at session end. Exit controls are not present in guidance bubbles. A Guide can be launched again after stopping. The Web Native Messaging host is browser-owned infrastructure: the extension establishes a persistent native connection on startup/installation and service-worker initialization, so the host may outlive individual learner sessions and remain running while no Guide is active. This is accepted current behavior; on-demand connection lifecycle is not a prerequisite for correct Guide operation. Publishing to the same output directory requires stopping processes that use the deployed files. The production extension and Native Messaging host no longer include the extension-native E2E command path or dedicated test pipe. A full 55-step post-change regression in both runtimes remains pending.

## Delivery
The Windows x64 framework-dependent package includes the learner and a complete Native Messaging host under `C:\GuideMe\NativeHost`. Chrome registration points to the packaged executable, not a build or temporary directory. Restart Chrome after registration.

```powershell
dotnet build src\DAP.App\DAP.App.csproj -c Release
.\scripts\Publish-Customer-Package.ps1 -Output C:\GuideMe
.\scripts\Register-WebNativeHost.ps1 -PackagePath C:\GuideMe
& "C:\GuideMe\DAP.exe" --check
```

Customer package includes `ChromeExtension` (unpacked extension files) and `Register-WebNativeHost.ps1`. On a new machine, load `ChromeExtension` using Chrome's **Load unpacked**, copy the actual extension ID from `chrome://extensions`, and run the packaged registration script with `-ExtensionId <ID>`. Do not assume the development-machine extension ID. The package still requires Chrome extension loading and host registration; copying the directory alone does not install either.
