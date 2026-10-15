# Application Context Architecture

## Purpose
An Application Context identifies the target application instance for a Guide Step. It is distinct from the Step's TargetDescriptor, which identifies a UI element within that application. Runtime must not guess when discovery is ambiguous.

## Persisted model
- `GuideApplicationContexts`: context key, owning Guide, runtime type.
- `ApplicationContextMatchers`: ordered matcher kind and value for a context.
- `GuideSteps.ApplicationContextKey`: reference to the Guide's context.
- `TargetDescriptor.Runtime`: runtime of the Step target; must agree with its context.

The model permits multiple named contexts, but support for executing across them is runtime-specific.

## Web implementation
The Web adapter passes persisted contexts to the GuideMe Chrome extension. It can discover an existing browser tab through supported matchers:
- `TitleEquals`
- `TitleContains`
- `UrlEquals`
- `UrlContains`
- `UrlHost`

The adapter retains context-to-tab identity and resolves the relevant frame/target during execution. Context matching must be unique. Tab replacement, DOM changes, and stale content-script endpoints require re-resolution rather than guessed selection.

## Windows implementation
The Windows runtime currently supports one Windows application context per Guide at startup. It resolves a unique top-level window using supported matchers:
- `WindowTitleContains`
- `AutomationId`
- `ProcessName`

When no persisted Windows context is defined, an explicit `--window-automation-id` can be used. Multiple Windows contexts, per-Step switching, delayed discovery, and rebinding are not implemented.

## Runtime scope
A Guide currently requires one enabled target runtime type. Cross-runtime transitions between Web and Windows within a single Guide are not implemented. Application Context persistence does not imply those capabilities are operational.

## Example contexts
The example Web Guide `sampleapp-web-guide` uses context `crm` and a local Web URL host matcher. The Windows Guide `sampleapp-windows-guide` uses `crm-windows` with the demo window automation ID `TestCrmMainWindow`. These are persisted example values, not hard-coded product rules.

## Session and ownership
DAP attaches to an already-running target application. It does not own the target's process lifetime. Guide completion removes learner UI and terminates the DAP learner; the target remains open. Browser Native Messaging infrastructure can persist independently of the learner session.

## Engineering boundaries
Application discovery, target discovery, session cleanup, and Guide advancement are separate responsibilities. Unimplemented context switching or session isolation must not be described as available functionality.
