# Architecture Guardian — Current Contract

Architecture Guardian is an optional, read-only development audit. It is **not** part of DAP product execution, deployment, or runtime dependencies.

## Current implementation

- GitHub Actions workflow: `.github/workflows/architecture-guardian.yml`.
- Read-only deterministic checker: `tools/architecture-guardian/check.py`.
- Runs for relevant pull requests and can be started manually with **Actions → Architecture Guardian → Run workflow**.
- Reports PASS/FAIL in the workflow job log. It checks the presence of the authoritative project Markdown documents and prohibits direct Playwright/Selenium package dependencies in production projects under `src/`.
- It does **not** inspect ChatGPT conversations, invoke another AI agent, perform semantic architecture review, or block direct commits to main. Branch protection is a separate repository setting.
- Existing project Markdown and current main code remain authoritative. No historical log is maintained here.

## Independent AI review — not yet enabled

A genuinely independent AI reviewer would require a separately executed model with read-only access to the latest authoritative Markdown, the proposed change, and the changed code. It must cite specific conflicting rules, distinguish verified violations from uncertainty, and never modify code. It cannot be represented as active until an actual independent invocation and its report are verified.

## Removal

Delete `.github/workflows/architecture-guardian.yml`, `tools/architecture-guardian/`, and this document. No production code or build configuration needs modification.
