# Application Context and Session Isolation Architecture

This document distinguishes implemented application-context capabilities from the cross-runtime and session-isolation requirements not yet implemented. Historical alternatives belong in Git history.

## Purpose

A DAP Guide may operate against an application that is already open when Learner starts, and a future Guide may move between multiple applications and runtimes while remaining one continuous Guide.

Representative flow:

```text
CRM Web
  |
  | learner action opens Billing
  v
Billing Windows
  |
  | later Guide Step returns to CRM
  v
CRM Web
```

Application discovery is separate from target discovery:

- Application Context answers: which application instance does this Step belong to?
- TargetDescriptor answers: which UI element inside that application is the Step target?

DAP must not use target locators as a substitute for explicit application identity when an Application Context is defined.

## Guide-level application contexts

A Guide may define one or more named application contexts.

Examples:

```text
crm
  Runtime = Web

billing
  Runtime = Windows
```

A Step references the context in which it executes.

Example:

```text
Step 1  -> crm
Step 2  -> crm
...
Step 20 -> crm
Step 21 -> billing
...
Step 30 -> billing
Step 31 -> crm
```

The intended lifecycle resolves an application context on first use, not necessarily at Guide startup. The current Windows implementation instead resolves its single context at Learner startup. This allows an application such as Billing to be opened naturally by a learner action in an earlier CRM Step.

## Persistence model

The persisted model uses two tables and one Step reference.

### GuideApplicationContexts

```sql
CREATE TABLE GuideApplicationContexts (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    GuideId INTEGER NOT NULL,
    Key TEXT NOT NULL,
    Runtime TEXT NOT NULL,
    FOREIGN KEY (GuideId) REFERENCES Guides(Id) ON DELETE CASCADE,
    UNIQUE (GuideId, Key)
);
```

`Runtime` identifies the application-discovery mechanism. For example, Web contexts are discovered through the browser adapter and Windows contexts through the Windows application/UI Automation boundary.

### ApplicationContextMatchers

```sql
CREATE TABLE ApplicationContextMatchers (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ApplicationContextId INTEGER NOT NULL,
    MatcherOrder INTEGER NOT NULL,
    Kind TEXT NOT NULL,
    Value TEXT NOT NULL,
    FOREIGN KEY (ApplicationContextId)
        REFERENCES GuideApplicationContexts(Id)
        ON DELETE CASCADE,
    UNIQUE (ApplicationContextId, MatcherOrder)
);
```

Matchers are persisted discovery criteria. The model permits more than one matcher without another schema change.

Matchers supported by current context resolvers:

```text
Web:
  TitleEquals
  TitleContains
  UrlEquals
  UrlContains
  UrlHost

Windows (single context at startup):
  WindowTitleContains
  AutomationId
  ProcessName
```

The exact supported matcher set is a Runtime capability and must be validated rather than silently ignored.

### GuideSteps

`GuideSteps` contains:

```text
ApplicationContextKey TEXT NULL
```

The value references a context key belonging to the same Guide.

The existing `TargetDescriptor.Runtime` remains in the current model. Application Context support does not require removing it. For a targeted Step, the context Runtime and target Runtime must agree; a mismatch is an invalid Guide definition.

## Resolution lifecycle

For every enabled Step that requires an application context:

```text
Step
  |
  v
ApplicationContextKey
  |
  v
Already resolved in this Learner run?
  |                     |
 yes                   no
  |                     |
  v                     v
Validate instance    Apply persisted matchers
still exists            |
  |                     v
  |                  Resolve unique instance
  |                     |
  +----------<----------+
             |
             v
       Execute Step inside
       resolved context
```

Resolution must remain deterministic. DAP must not guess when persisted matching criteria produce an ambiguous result.

If a previously resolved instance no longer exists, DAP may re-run the persisted matchers for that context.

## Runtime state is not Guide data

Concrete runtime identities are transient and must not be stored in the Guide database.

Examples:

```text
crm
  -> browser window/tab identity

billing
  -> HWND / Windows application identity
```

These values belong to the current Learner execution state.

Conceptually:

```text
Persisted Guide
  -> ApplicationContextDefinition
     -> what application to find and how to find it

Learner runtime memory
  -> ResolvedApplicationContext
     -> concrete application instance found in this run

Guide Step
  -> ApplicationContextKey
     -> context required by this Step
```

This allows a Guide to leave Billing, return to CRM, and later return to the same Billing instance while that instance is still valid.

Persisting runtime identity for process restart/resume is a separate session-state concern and is not part of the Guide definition.

## Cross-runtime orchestration

The data model must support one Guide containing Steps associated with different application contexts and runtimes.

The planned cross-runtime execution architecture is:

```text
DAP Learner
    |
Guide Orchestrator
    |
    +-------------------+
    |                   |
Web Runtime       Windows Runtime
    |                   |
CRM context       Billing context
```

The orchestrator owns continuous Guide position and resolved application-context state across runtime transitions. Runtime-specific components continue to own target resolution and learner semantics for their Step.

Changing from a Web Step to a Windows Step does not itself launch the Windows application. The learner action or target application's normal business behavior may cause that transition. DAP resolves the next required application context when the Guide reaches the corresponding Step.

The external launch contract remains Guide-driven:

```text
DAP.exe --guide <GuideId>
```

Current implementation note: the existing launcher still requires enabled targeted Steps in a Guide to belong to exactly one Runtime. Cross-runtime orchestration described here is the target architecture and is not yet implemented.

## Citrix and multi-user isolation

DAP must support multiple users connected to the same Citrix host in separate operating-system sessions.

The isolation rule is:

> Guide definitions may be shared. Runtime state and Runtime communication channels must be isolated to the user/session running that Learner instance.

Conceptually:

```text
Citrix Session A
  DAP.exe
  CRM A
  Billing A
  resolved contexts A
  Web transport A

Citrix Session B
  DAP.exe
  CRM B
  Billing B
  resolved contexts B
  Web transport B
```

A DAP instance must never bind to another Citrix user's application context.

### Windows

Windows application discovery and UI Automation must remain scoped to the operating-system/Citrix session in which DAP is running. Once a concrete application window is resolved, target resolution operates under that application's window root.

Concrete window identity is runtime state, not shared Guide data.

### Web

The browser Extension and Native Messaging host execute in the browser/user context, but the .NET transport must also be isolated.

Current implementation uses fixed production and E2E named-pipe names:

```text
dap-web-runtime-v1
dap-web-e2e-v1
```

Therefore the current Web transport must not yet be considered fully validated for concurrent multi-user Citrix execution.

The target architecture requires a per-session transport identity so that a Native Host belonging to one Citrix session can connect only to the corresponding DAP Runtime channel.

Conceptually:

```text
Citrix Session A
  -> Native Host A
  -> session-specific pipe A
  -> DAP A

Citrix Session B
  -> Native Host B
  -> session-specific pipe B
  -> DAP B
```

The exact transport naming/authorization mechanism must be deterministic and derived from the local execution session. It must not depend on Guide data.

## Current Web context behavior

The persisted model and Web implementation support named Application Contexts. `GuideApplicationContexts`, `ApplicationContextMatchers`, and `GuideSteps.ApplicationContextKey` are persisted in SQLite and exposed through the Guide repository. The Web adapter supplies the contexts to the Extension, which resolves and retains browser tabs per context key. Supported Web matcher kinds are `TitleEquals`, `TitleContains`, `UrlEquals`, `UrlContains`, and `UrlHost`; matching must resolve uniquely, without guessing. Web context-aware commands include target observation, interaction, bubble display/cleanup, capture, and centered informational Steps. The current TestCRM Web Guide has 55 Steps referencing context `crm` with matcher `UrlHost:localhost:5200`.

A standalone TestCRM Web host and independently launched `DAP.exe` have been verified through full Manual Guide completion against an already-open Chrome tab. DAP exits at completion without terminating the application or browser.

Windows named Application Context resolution currently supports exactly one Windows context per Guide at startup, with a unique top-level window match. The Windows canonical Guide persists `crm-windows` with `AutomationId: TestCrmMainWindow` and references it from all 55 Steps. Independent Windows host and Learner execution was reported successful in user testing. Windows multi-context switching, delayed discovery, and rebinding are not implemented. Cross-runtime Guide orchestration and concurrent Citrix-session isolation remain unverified or unimplemented.

## Design rules

1. Application discovery and target discovery are separate concerns.
2. Application contexts are persisted Guide definitions.
3. Concrete tab/window/process identities are runtime state.
4. Contexts are resolved on first use and may be reused later.
5. A Guide may define multiple application contexts.
6. Runtime transitions do not split one business process into separate Guides.
7. DAP never guesses between ambiguous application instances.
8. Runtime state is isolated per Learner execution and per Citrix/OS session.
9. Shared Guide persistence must not contain transient HWND, tab ID, process ID, or equivalent runtime identity.
10. Current runtime semantics remain owned by the production Runtime; Application Context resolution must not create a second validation or progression engine.

## Execution modes and Windows application binding

The production learner accepts `DAP.exe --guide <GuideId> --mode manual|hybrid`. Application identity is persisted in Guide Application Context matchers, not inferred from test drivers. Windows resolves the target application window and then resolves individual Step targets through UI Automation. Hybrid only sets explicitly persisted AutomationValue on eligible writable targets; Edit controls are focused, set, and committed through a verified TAB focus transition. Existing runtime validation controls advancement. Manual and Hybrid share the same Guide, context, and completion engine. No database schema change is required. Web production Hybrid now has an extension-backed persisted-value executor; regression is pending.

## Shared guide execution policy

`DAP.Core.Guides.GuideRunPlan` is the common runtime-neutral owner of ordered Guide Steps, start-step selection, disabled-step lookahead, captured values, runtime capture-token substitution, and asynchronous step lifecycle orchestration. Both the Web adapter guide runtime and Windows guide runtime use it. Target resolution, active-step validation, bubble presentation, and UI-specific actions remain in their respective runtimes/adapters; a single unified active-step validation/presentation engine has **not** yet been implemented. Session termination and build-lock preflight are handled by the shared DAP.App host. Web tab/window activation remains an open gap; production Web Hybrid value automation is implemented but unverified. This is current architecture, not a completed full runtime unification.

## Shared value-validation policy

`DAP.Core.Guides.GuideValidationPolicy` now evaluates persisted `value-equals` and `value-not-empty` rules using observed values supplied by runtime adapters. `WindowsValidationEvaluator` delegates these checks to Core while retaining Windows UI Automation `ValuePattern` access. The Web browser adapter also delegates value checks to Core, preserving its existing missing-expected-value behavior. Web event/commit handling, completion conditions, target resolution, and bubble presentation are unchanged. This is a focused first step, not a completed unified active-step validation engine. A fresh build and Web/Windows regression are still required.

### Shared completion-condition policy

`DAP.Core.Guides.GuideCompletionPolicy` evaluates `target-exists`, `target-not-exists`, `target-enabled`, and `value-equals` from runtime observations. Both Web and Windows now delegate these four checks to Core. Target inspection/resolution remains adapter-specific; Windows `target-replaced` still compares UIA element identities locally. Web retains its previous treatment of unresolved targets for `target-not-exists`. Build and regression verification of this change are pending.

### Production Web Hybrid implementation (pending regression)

The production Web learner now accepts `DAP.exe --guide <GuideId> --mode hybrid`. After target resolution and presentation readiness, it applies persisted `AutomationValue` once through the extension to a uniquely resolved writable text input/textarea, using focus, native value setter, input/change events and blur. The normal validation/commit and completion-condition pipeline still controls advancement. Steps without `AutomationValue` remain user-operated. The extension command is production-scoped and does not invoke TestCRM test-driver operations. No claim of end-to-end PASS is made until a local build and full Web/Windows Manual/Hybrid regressions are reported. Non-text value automation and browser tab activation are not implemented.

Standalone TestCRM Web Host opens the application in Chrome automatically after the five-second readiness check. DAP.exe remains a separate product process and does not own the target application's browser or servers. This host change requires a local Windows execution check.
