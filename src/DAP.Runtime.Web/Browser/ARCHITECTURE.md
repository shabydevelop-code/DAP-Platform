# Web Browser Adapter

The Web learner has one behavioral owner: DAP Runtime.

The browser adapter is a transport/platform boundary only. It must not own guide
sequencing or invent alternate validation, context, reconciliation, capture, or
bubble behavior.

## Implementations

- Playwright adapter: regression baseline during migration.
- Extension adapter: production browser implementation.

## Migration rule

Existing Web learner behavior is the specification. In particular, value
validation commits only on the same natural commit events already implemented
by the current runtime (text edit followed by blur; discrete control change).
The extension reports browser facts/events to DAP Runtime; DAP Runtime decides
whether a Step completes and when the next Step starts.

Playwright is not removed until the extension adapter demonstrates parity with
the existing Web E2E scenarios.
