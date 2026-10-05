# DAP Web Runtime Extension

This project is the production browser adapter used by the Web Learner path.

The extension is **not** a Guide engine. Guide sequencing, validation decisions,
completion-condition policy, runtime capture/materialization, and Step advancement
remain in the .NET Runtime.

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

The former JSONL command/response/event journals were migration scaffolding and
are no longer the active transport.

## Behavioral contract

The existing Playwright Web Runtime is the behavioral specification during this
migration. The extension must preserve its learner semantics rather than invent a
second Web runtime model.

Current implemented behavior includes:

- target resolution for CSS, text, label, and role locators;
- anchor relations: ancestor/context, descendant, sibling, and nearby;
- explicit ambiguous/not-found results; never choose a candidate by guess;
- explicit `FrameContext` routing and frame re-resolution after replacement;
- content readiness probing and idempotent reinjection when a frame has no receiver;
- safe handling of invalidated extension contexts after extension reload;
- DOM/target re-resolution through reconciliation;
- natural validation commits:
  - text edit followed by blur;
  - discrete-control change;
  - click event;
- click acknowledgement/replay for browser-default navigation/submission capable targets;
- live validation rebinding when the current DOM target is replaced;
- valid non-click commits remain latched while persisted completion conditions are pending;
- top-level visual proxy bubbles when a child frame cannot physically contain the bubble;
- explicit-handle dragging for regular/promoted/centered/completion surfaces;
- manual proxy position remains authoritative after dragging;
- `grabbing` remains active for the full pointer-drag lifetime;
- centered informational Steps and explicit Guide completion.

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

## Reference implementation use

`Generic-Web-Training-Platform` was inspected as a reference for proven
extension messaging/lifecycle patterns such as readiness probing, reinjection,
frame communication, and page lifecycle handling.

DAP deliberately does **not** copy GWTP's extension-owned training-engine model.
DAP keeps learner policy in .NET.

## Current verification status — 2026-10-05

A manual production-path run of the persisted
`testcrm-web-canonical-workflow` completed **54/54 Steps** through the extension
adapter, including server-backed FieldChange, reload/document replacement,
conditional targets, validation rejection/recovery, runtime capture, context
return, deletion, the cross-frame Header Step, and final Guide completion.

Focused Step 54 verification also confirmed promoted-bubble dragging and stable
`grabbing` cursor behavior.

This is not yet a claim that Playwright has been removed from the repository or
that every automated Web browser/mode regression has been re-run through the
extension path.
