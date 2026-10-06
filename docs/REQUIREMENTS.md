# Requirements

## Product quality

1. DAP-Platform is a production-target product, not a proof of concept.
2. Production architecture must not depend on temporary POC infrastructure or legacy GWTP implementation details.

## Application

1. Provide a Windows desktop application named DAP.exe.
2. Provide Learner and Editor modes in the same product.
3. Keep application/core contracts independent from WPF where practical.

## Languages

1. Support Hebrew GUI.
2. Support English GUI.
3. Allow the user to choose GUI language.
4. Apply RTL layout for Hebrew and LTR layout for English.
5. Keep guide-content language independent from GUI language.
6. Do not hard-code translatable GUI text in views.

## Guides

1. Support Web-only guides.
2. Support Windows-only guides.
3. Support hybrid guides containing both Web and Windows steps.
4. Maintain learner progress.
5. Support runtime-specific target descriptors behind a common Step model.
6. Support validations that prevent advancement until the required learner action/state is satisfied.
7. Distinguish Step ordering from application/browser navigation.
8. Support an explicit Step advance mode: automatic after successful validation or manual for informational Steps.
9. Do not expose Previous as unconditional StepOrder navigation.
10. Expose Previous only when the active runtime determines that the previous Step can be safely rendered in the current context.
11. Preserve sufficient context/navigation metadata to support page changes, application changes, and Web/Windows runtime transitions.
12. Support targetless informational Steps presented as centered learner bubbles with an explicit confirmation action.
13. Do not require or invent a target descriptor for a purely informational Step.
14. Present Guide completion through the same centered DAP bubble presentation family on Web and Windows.

## Web

1. Use the DAP browser extension + Native Messaging + .NET Web Runtime as the single production and E2E browser-access architecture.
2. Do not introduce a second browser-control stack for tests or production.
3. Browser DOM actions and inspection used by Web E2E must cross the DAP extension boundary.
4. Observe learner actions; do not execute guide actions on behalf of the learner during normal guide execution.
5. Support dynamic DOM updates and target re-resolution.
6. Support frames, iframe replacement, and navigation.
7. Support asynchronous application/server behavior.
8. Support Editor recording/target capture through production-capable browser interfaces.
9. Discover the installed Chrome/Edge profile containing the DAP extension without requiring a browser-mode selector.
10. Fail explicitly when no matching extension profile exists or when multiple profiles are ambiguous.
11. Explicitly package or validate the DAP browser extension, Native Messaging host, and supported browser requirements.
12. Keep target identity, validation, completion, capture, and progression owned by persisted Guide data plus the production Runtime; E2E must remain only the synthetic learner/action layer.

## Windows

1. Use Microsoft UI Automation for the production Windows Runtime.
2. Support target discovery and re-discovery.
3. Support guide overlays/bubbles associated with native targets.
4. Support observation and validation of learner actions.
5. Support Editor target selection.

## Data

1. Keep Core independent of the concrete database.
2. Provide SQLite as the first database provider.
3. Allow additional database providers without rewriting Core/runtime business logic.
4. Treat persisted Guide data as authoritative after initialization: Seed initializes, DB owns, Runtime consumes.

## E2E

1. Keep the default synchronization timeout ceiling at 5 seconds.
2. Require explicit user approval before increasing any E2E timeout above 5 seconds.
3. Prefer real application readiness signals over fixed delays.
4. Keep Fast and Visual on the same business/learner action path; Visual may add cursor movement/pacing only.
5. Do not add hidden E2E selectors or completion rules to compensate for persisted Guide/runtime behavior.
6. Keep automated Web execution valid only if the same persisted Guide can complete through the production learner without the E2E runner connected.

## Deployment

1. Target Windows.
2. Target .NET 8.
3. Assume .NET 8 Desktop Runtime is installed on target machines.
4. Application distribution is framework-dependent.
5. Detect a missing required runtime and present a clear installation/startup error.
6. Do not require Python.
7. Handle or validate all additional production runtime dependencies explicitly.

## Documentation

1. Maintain project context and current status in Markdown.
2. Record architectural decisions in Markdown.
3. Update documentation alongside significant code changes.
