# Current Status

## Current verified baseline — 2026-10-06

The canonical persisted Guides contain 54 Steps on both Web and Windows.

### Web

Verified current state:

- Runner-free manual Web learner execution: **54/54 PASS**.
- Guided Fast automated execution through the browser extension path: **54/54 PASS**.
- The Web browser path is the installed DAP Extension + Native Host + .NET Runtime.
- Chrome/Edge profile discovery is automatic; the old browser selector environment variable is not part of the active path.
- Target resolution, validation, completion conditions, capture, and Step progression are owned by persisted Guide data plus production Runtime logic.
- E2E is only the synthetic learner/action layer and must not add hidden target selectors or completion rules.
- The 5-second E2E timeout ceiling is unchanged.

Current Web production path:

```text
DAP.exe / AdapterWebGuideRuntime
    ↕
ExtensionWebBrowserAdapter
    ↕ Named Pipe
DAP.Runtime.Web.NativeHost
    ↕ Native Messaging
Manifest V3 service worker
    ↕
content-runtime.js
    ↕
Browser DOM
```

### Guided Fast verification

The latest full Guided Fast run completed the persisted 54-Step TestCRM Web Guide successfully.

The run verified:

- extension profile auto-discovery;
- Native Host handshake;
- DAP startup from the persistent SQLite Guide database;
- production Web bubble presentation;
- rejection of invalid committed values;
- blur-based text commit semantics;
- automatic Step transition;
- server-backed navigation and iframe replacement;
- the full canonical Customer -> Site -> Case -> Lead workflow;
- final Guide completion.

A transient E2E frame-routing failure was fixed without changing Guide data or product validation semantics. The E2E frame helper now resolves the live TestCRM iframe element through the extension-native frame-path mechanism rather than trusting a stale browsing-context name.

### Web CPU baseline

Manual Chrome Task Manager measurements for the TestCRM tab while idle:

- original 100 ms steady reconciliation: roughly 13–15% CPU;
- 250 ms stable reconciliation: roughly 4.5% CPU;
- 500 ms stable reconciliation: roughly 2% CPU.

Current accepted cadence:

- recovery states: 100 ms;
- stable resolved Step: 500 ms.

The earlier attempt to make the stable learner fully event-driven reduced CPU further but caused observable bubble regressions and was rejected. The accepted architecture keeps the proven reconciliation behavior and only reduces the stable polling cadence.

### Web bubble baseline

Current accepted behavior includes:

- stable bubble instance while Step/target remains unchanged;
- hide while the target is outside the visible viewport;
- reappearance when the target returns;
- explicit drag handle;
- `grabbing` cursor for the full drag lifetime;
- manual placement remains authoritative for the active Step;
- top-level proxy presentation for constrained child-frame targets;
- centered informational Steps;
- explicit Guide-completion bubble.

### Web frame/runtime rule

TestCRM may replace or promote iframes during server-backed interactions. DOM and frame identities are transient.

The Runtime and extension therefore re-resolve the live frame/target from persisted semantics. Neither product logic nor E2E may depend on a historical browser-frame name remaining stable.

## Windows

The Windows Learner Runtime uses Microsoft UI Automation and WPF-native learner presentation.

Verified baseline includes:

- persisted 54-Step Guide;
- full production learner coverage;
- runtime capture;
- modal targeting;
- target-relative bubble presentation;
- explicit-handle dragging;
- real text edit + focus-loss commit semantics;
- discrete-control selection/change semantics;
- one-time initial viewport adjustment;
- completion UI.

Windows and Web share the same runtime-neutral Guide/Target/Validation contracts while using platform-specific adapters.

## Canonical run modes

The canonical Web runner supports:

```text
--manual
--guided
--manual-from-step N
--unguided
--visual-from-step N
```

For automated Guided execution:

```powershell
$env:DAP_E2E_MODE="fast"
```

or:

```powershell
$env:DAP_E2E_MODE="visual"
```

Chrome and Edge are supported browsers, not separate DAP run modes.

## Persistent Guide rule

Normal execution uses the persistent Guide database, normally:

```text
C:\ProgramData\DAP\Data\DAP.db
```

Rule:

**Seed initializes. DB owns. Runtime consumes.**

A seed update does not silently overwrite an existing persisted Guide. Reset is explicit.

## Closed-target rule

DAP must support closed third-party target applications.

Runtime behavior must rely only on production-observable interfaces. TestCRM source may be inspected during development for diagnosis, but source knowledge may not become a runtime target or validation oracle.

## E2E fidelity rule

The automated runner may execute learner actions and assert results, but it must not provide facts that should already be derived from the persisted Guide and production Runtime.

If a manually successful Guide requires an extra target selector, hidden validation condition, special transition rule, or application-specific bypass only when automated, the E2E architecture is wrong and must be fixed rather than extending Guide semantics in the runner.

## Timeout rule

The default maximum E2E synchronization timeout remains 5 seconds.

Any increase above 5 seconds requires explicit user approval before implementation, including temporary diagnostics.

## Process isolation

Web and Windows E2E runners build owned long-lived processes into unique temporary run directories rather than executing from reusable repository build output.

The Web runner preflights ports 5200 and 5201 and fails if an unknown process already owns them. It does not kill arbitrary port owners.

## Current next work

1. Verify the remaining Web run modes on the extension-native architecture, starting with `manual-from-step`.
2. Verify `visual-from-step` using the same action path with visible cursor movement/pacing only.
3. Verify `unguided` without introducing parallel target/completion knowledge in the runner.
4. Keep the runner-free manual 54-Step Guide as the product acceptance reference.
5. Continue profiling only if additional idle CPU reduction is needed; do not trade learner/bubble correctness for lower polling frequency.

## Local repository size

Local cleanup removed 31 regenerable `bin`/`obj` build directories with no locked paths remaining.

Measured cleanup result:

```text
After:  15.2 MB
Freed:  737.4 MB
```

This confirms that the previous large working-folder footprint was overwhelmingly generated build output rather than source code. The cleanup script intentionally leaves source, Git history, the persistent DAP database, and the global NuGet cache untouched.
