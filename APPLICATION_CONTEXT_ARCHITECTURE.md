# Application Context and Session Isolation Architecture

This document defines the current target architecture for application-context resolution and per-user runtime isolation. It describes the design to be implemented; where the current code differs, that difference is stated explicitly. Historical alternatives belong in Git history.

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

DAP must not use target locators as a substitute for explicit application identity once Application Context support is implemented.

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

An application context is resolved on first use, not necessarily at Guide startup. This allows an application such as Billing to be opened naturally by a learner action in an earlier CRM Step.

## Persistence model

The target persistence model adds two tables and one Step reference.

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

Initial useful matcher kinds include:

```text
Web:
  TitleEquals
  TitleContains
  UrlEquals
  UrlContains
  UrlHost

Windows:
  WindowTitleContains
  AutomationId
  ProcessName
```

The exact supported matcher set is a Runtime capability and must be validated rather than silently ignored.

### GuideSteps

`GuideSteps` gains:

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

The intended execution architecture is:

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
DAP.exe --learner --guide <GuideId>
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

The current browser Extension already retains a resolved production tab identity in memory after it uniquely identifies a production tab. This is useful runtime behavior, but it is currently a single production-tab binding rather than the named multi-context model defined in this document.

Application Context implementation will generalize that concept so multiple named contexts can coexist in one Learner run.

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
