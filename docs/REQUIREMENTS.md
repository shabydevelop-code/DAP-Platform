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

## Web

1. Use Microsoft Playwright for .NET as the production Web Runtime.
2. Integrate Playwright directly into the .NET product; Python must not be required on target machines.
3. Observe learner actions; do not execute guide actions on behalf of the learner during normal guide execution.
4. Support dynamic DOM updates and target re-resolution.
5. Support frames and navigation.
6. Support asynchronous application/server behavior.
7. Support Editor recording/target capture.
8. Explicitly package or validate required browser/Playwright deployment dependencies.

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
