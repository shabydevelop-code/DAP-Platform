# Application Context Architecture

## Purpose
An Application Context identifies the target application instance for a Guide Step. It is distinct from the Step's TargetDescriptor, which identifies a UI element within that application. Runtime must not guess when discovery is ambiguous.

## Persisted model
- `GuideApplicationContexts`: context key, owning Guide, runtime type.
- `ApplicationContextMatchers`: ordered matcher kind and value for a context.
- `GuideSteps.ApplicationContextId`: foreign key to `(GuideId, ContextId)` in `GuideApplicationContexts`.
- `GuideSteps.Runtime` and the context runtime must describe the same execution platform. Element definitions and locators are stored in `GuideElements`, `ElementLocators`, and `StepElementUsages`.

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
The Windows runtime resolves a persisted Windows application context for the active Guide step. It resolves a unique top-level window using supported matchers:
- `WindowTitleContains`
- `AutomationId`
- `ProcessName`

Context matching must identify an unambiguous target window. Do not assume support for unverified delayed discovery or multiple simultaneous Windows windows.

## Runtime scope
Web-only, Windows-only and mixed Web–Windows–Web Guides are supported. The persisted application context of each step identifies which application the learner must attach to. The sample mixed smoke Guide has been executed successfully.

## Example contexts
The example Web Guide `sampleapp-web-guide` uses context `crm` and a local Web URL host matcher. The Windows Guide `sampleapp-windows-guide` uses `crm-windows` with the demo window automation ID `SampleAppMainWindow`. These are persisted example values, not hard-coded product rules.

## Session and ownership
GuideMe attaches to an already-running target application. It does not own the target's process lifetime. Guide completion removes learner UI and terminates the GuideMe learner; the target remains open. Browser Native Messaging infrastructure can persist independently of the learner session.

## Engineering boundaries
Application discovery, target discovery, session cleanup, and Guide advancement are separate responsibilities. Capabilities beyond the verified context-switching flow must be checked against current code and live execution.
