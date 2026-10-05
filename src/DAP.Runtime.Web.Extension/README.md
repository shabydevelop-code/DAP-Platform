# DAP Web Runtime Extension POC

This project starts the migration of the DAP Web learner runtime away from Playwright.

## Scope of this first slice

- Manifest V3 content runtime.
- Runs in every permitted frame.
- Browser-native target resolution for the existing Web locator strategies: CSS, text, label and role.
- Existing CSS anchor relations: ancestor/context, descendant, sibling and nearby.
- Explicit ambiguous-target result; the runtime never guesses.
- MutationObserver foundation for rerender/reconciliation.

## Intentionally not changed yet

- DAP.Core models and persistence.
- Existing Playwright runtime.
- Existing E2E runner.
- Windows runtime.
- Bubble rendering, validation, guide state and cross-frame descriptor routing are the next migration slices.

The extension is deliberately additive. Playwright remains the regression oracle until the extension passes the existing Web scenarios.
