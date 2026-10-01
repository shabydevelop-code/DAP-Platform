# Architecture Decisions

## Runtime and persistence boundary

The DAP Runtime must remain independent of the physical database provider. SQLite is the current persistence implementation, not a runtime contract.

The domain/Core model is defined before committing to a physical database schema. Persistence adapters map the runtime-neutral model to SQLite or a future database provider.

## TargetDescriptor

A guide step references a runtime-neutral TargetDescriptor. It contains:
- Runtime: Web or Windows
- Frame/context information where applicable
- A primary locator
- Zero or more anchors/context constraints

Target resolution is candidate discovery followed by anchor matching and uniqueness verification. Ambiguous or missing targets are explicit resolution results; the runtime must not guess.

The physical storage representation of TargetDescriptor, including whether anchors are normalized tables or serialized JSON, remains an implementation decision until the Core model is stable.

## Current implementation order

1. Define Core/domain contracts and models.
2. Define persistence interfaces.
3. Implement SQLite persistence adapter.
4. Implement Learner Runtime and bubble lifecycle.
5. Integrate Web Runtime.
6. Integrate Windows Runtime.
7. Build Instructor Runtime against the stable contracts.
