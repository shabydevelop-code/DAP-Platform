# Architecture Guardian — Current Contract

Architecture Guardian is an optional, read-only development audit. It is not a DAP product dependency.

## Current implementation

- Workflow: `.github/workflows/architecture-guardian.yml`.
- Deterministic checks: `tools/architecture-guardian/check.py`; run for relevant pull requests and manual workflow dispatch.
- Independent AI review: `tools/architecture-guardian/review.py`; runs on relevant same-repository pull requests, using the current Markdown rules and the proposed code diff.
- The AI reviewer does not change files or approve merges. Its classifications are advisory; inability to complete an invoked review is an error, not a pass.
- AI review requires the repository Actions secret `OPENAI_API_KEY`. If absent, the job explicitly reports `AI REVIEW NOT RUN`. Do not interpret a green deterministic job as an AI approval.
- The AI step is not run on fork pull requests, to avoid exposing secrets to untrusted contributions. Manual workflow dispatch runs deterministic checks only.
- Reports are visible in GitHub → Actions → Architecture Guardian → selected run → audit.
- The workflow does not monitor ChatGPT conversations, run before direct commits to main, or block direct pushes. Branch protection and mandatory pull requests require separate repository configuration.
- Repository source and current-state Markdown remain authoritative. This document does not maintain history.

## Setup

In GitHub repository Settings → Secrets and variables → Actions, create a repository secret named `OPENAI_API_KEY`. This key is used only by the optional review step and is not included in DAP production code. The reviewer sends the relevant diff and current project Markdown to the configured external model API; do not enable it if those materials must not leave GitHub.

## Removal

Delete `.github/workflows/architecture-guardian.yml`, `tools/architecture-guardian/`, and this document, then remove the optional repository secret. DAP product code and build configuration require no changes.
