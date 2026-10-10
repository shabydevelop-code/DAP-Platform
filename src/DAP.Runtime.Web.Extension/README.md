# DAP Web Runtime Extension

This project is the production browser adapter used by the Web Learner path.

The extension is not a Guide engine. Guide sequencing, validation decisions, completion-condition policy, runtime capture/materialization, and Step advancement remain in the .NET Runtime.

## Production path

```text
DAP.exe / AdapterWebGuideRuntime
    ↕
ExtensionWebBrowserAdapter
    ↕ Named Pipe
DAP.Runtime.Web.NativeHost
    ↕ Chrome Native Messaging
Manifest V3 service worker
    ↕
content-runtime.js in the resolved frame
    ↕
target DOM
```

The former JSONL command/response/event journals were migration scaffolding and are no longer the active transport.

## Behavioral contract

The extension path is the single Web browser-access architecture for production learner execution. It must preserve the persisted Guide semantics owned by the .NET Runtime and must not invent a second Web runtime model.

Current implemented behavior includes:

- target resolution for CSS, text, label, and role locators;
- anchor relations: ancestor/context, descendant, sibling, and nearby;
- explicit ambiguous/not-found results; never choose a candidate by guess;
- explicit `FrameContext` routing and frame re-resolution after replacement;
- content readiness probing and idempotent reinjection when a frame has no receiver;
- safe handling of invalidated extension contexts after extension reload;
- DOM/target re-resolution through reconciliation;
- natural validation commits: text field focus followed by blur (including unchanged values), discrete-control change, and click event;
- click acknowledgement/replay for browser-default navigation/submission-capable targets;
- live validation rebinding when the current DOM target is replaced;
- valid non-click commits remain latched while persisted completion conditions are pending;
- top-level visual proxy bubbles when a child frame cannot physically contain the bubble;
- explicit-handle dragging for regular, promoted, centered, and completion surfaces;
- manual proxy position remains authoritative after dragging;
- `grabbing` remains active for the full pointer-drag lifetime;
- centered informational Steps and explicit Guide completion;
- bubble visibility follows target viewport visibility.

## Extension/runtime ownership boundary

The service worker owns browser routing and content-script lifecycle mechanics.

The content runtime may:

- resolve DOM candidates;
- observe browser/DOM events;
- render/hide learner surfaces;
- report browser facts and learner events.

The content runtime must not:

- sequence Guide Steps;
- decide that a Step is complete;
- interpret business completion policy independently from the .NET Runtime;
- persist Guide progress;
- become dependent on target-application source code or private APIs.

