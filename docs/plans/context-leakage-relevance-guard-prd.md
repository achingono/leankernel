# PRD: Context Leakage and Relevance Guard for Identity-Derived Prompting

## Overview

Fix response skew caused by stale or transient identity context (for example, one-off prior conversation requests such as song selection tasks) and prevent internal diagnostic details from leaking into normal responses.

## Problem Statement

Recent responses pulled in irrelevant context from prior conversations and included internal implementation/diagnostic details. Investigation identified two primary leakage vectors:

1. `SystemPromptBuilder` injects full `USER.md` and capability-gap prompt content verbatim.
2. `UserConfigurationStep.SyncFromWikiAsync` blindly merges all `who-user-profile` and `what-user-preferences` claims into `USER.md`, including transient, task-like claims.

## Goals

1. Keep only durable user identity/preference context in prompt injection.
2. Exclude one-off requests and transient scheduling/task instructions from profile sync.
3. Ensure internal diagnostics do not appear in normal user-facing replies.
4. Preserve existing onboarding and stable preference behavior.

## Non-Goals

1. Rewriting the full wiki extraction architecture.
2. Introducing ML-based relevance classification in this iteration.
3. Changing routing/provider policy.

## Reviewed Plan (Cross-Model Review Applied)

This plan was reviewed with a different model before implementation. Applied outcomes:

1. Prefer **allowlist-based durable section filtering** for `USER.md` prompt injection over broad blocklists.
2. Add **durability heuristics** for wiki-fact sync to exclude imperative, date-bound, and question-like claims.
3. Keep capability-gap utility but avoid exposing raw diagnostic phrasing in prompt context.
4. Add tests for mixed durable/transient fact sets and prompt sanitization boundaries.

## Functional Requirements

### FR-1 USER.md Prompt Sanitization

- In `SystemPromptBuilder`, sanitize loaded `USER.md` before prompt assembly.
- Keep durable sections (identity/preferences/communication/priorities/tools).
- Exclude transient/ad-hoc sections and imperative task lines.

### FR-2 Internal Detail Suppression

- Ensure injected capability-gap prompt context is phrased as non-disclosable internal guidance, not raw diagnostics.
- Add explicit policy line in system prompt: do not disclose internal diagnostics unless explicitly asked.

### FR-3 Durable Fact Filtering During USER.md Sync

- In `UserConfigurationStep.ExtractAndMergeWikiFacts`, filter claims before merging:
  - Exclude task-like imperatives (e.g., “help me …”),
  - Exclude temporal one-offs (“tomorrow”, “this coming”, specific date-bound requests),
  - Exclude question-like prompts.
- Keep stable profile/preferences/tooling facts.

## Test Plan

1. `SystemPromptBuilder` test: transient sections/lines in `USER.md` are not injected.
2. `SystemPromptBuilder` test: durable preference sections remain injected.
3. `UserConfigurationStep` sync test: mixed wiki facts only persist durable facts.
4. Regression test: existing onboarding/sync initialization flow remains successful.

## Validation

Run full repository validation sequence after implementation:

1. `dotnet restore src/LeanKernel.sln`
2. `dotnet build src/LeanKernel.sln --no-restore -v minimal`
3. `dotnet test src/LeanKernel.sln --no-build -v minimal`
4. `scripts/quality/test-coverage.sh`
5. `scripts/quality/sonarqube-scan.sh`
6. `docker compose build`

## Acceptance Criteria

1. Responses no longer include irrelevant one-off prior-conversation tasks from identity context.
2. Responses do not include internal implementation/diagnostic details unless explicitly requested.
3. USER profile sync excludes transient task-like claims.
4. All quality gates pass.
