# Knowledge UI Retrieval and Wiki Fact Capture PRD

## Problem Statement

The Knowledge page can show **"No pages available"** even when wiki content exists, and recent chat-session knowledge may not become visible in browse flows. This creates user-facing uncertainty about whether wiki data is saved and retrievable.

## Scope

In scope:
- `KnowledgeUiService` browse resilience and parsing compatibility
- `LegacyFunctionCallChatClient` compatibility parsing for fenced legacy payloads
- Unit tests for the above behavior
- Feature documentation update for knowledge browse fallback behavior

Out of scope:
- Replacing GBrain MCP transport
- Reworking learning extraction strategy/modeling
- Broad UI redesign of `/knowledge`

## Implementation Plan

1. **Diagnose first (code + behavior path)**
   - Confirm browse empties when `list_pages` is unavailable/fails and `_knownPages` has not been primed.
   - Confirm which response payload forms are accepted by legacy function-call parsing.

2. **Knowledge browse reliability**
   - In `KnowledgeUiService.BrowsePagesAsync`, keep provider-backed `list_pages` as primary.
   - If `list_pages` is unavailable/fails/returns empty, use a bounded discovery fallback via `IKnowledgeService.SearchAsync` with fixed seed queries and aggregate unique slugs into `_knownPages`.
   - Preserve degraded-status messaging and return paged/sorted results from discovered pages.

3. **Browse result parsing tolerance**
   - Expand `ParseBrowseResult`/`ParsePageSummary` to accept additional common response shapes (including nested total values and alternate slug/key fields) while preserving strict null/type checks.

4. **Session fact capture compatibility**
   - In `LegacyFunctionCallChatClient`, accept fenced JSON payload wrappers (e.g., ```json ... ```), then apply existing strict legacy object validation (`type`, `name`, `parameters` only).
   - Keep current safety behavior: if tool execution fails, preserve original model response.

5. **Testing**
   - Add `KnowledgeUiService` unit tests for:
     - fallback discovery when browse listing is unavailable
     - parse tolerance for alternate browse payload shapes
   - Add/extend compatibility tests for:
     - fenced legacy payload parsing and tool execution path
     - non-legacy payload rejection remains intact

6. **Documentation**
   - Update `docs/features/ui-knowledge.md` to document the fallback discovery path and degraded browse expectations.

## Design Review (Cross-Model)

This plan was reviewed by a different model (`claude-sonnet-4.5`) before implementation. Review outcomes applied:
- Retained diagnosis-first approach.
- Kept strict safety checks for legacy payload handling.
- Added emphasis on test coverage for browse fallback and payload parsing.
- Limited changes to targeted surfaces to avoid speculative architecture shifts.

## Risks and Mitigations

- **Risk:** seed-query fallback may not discover every page.
  - **Mitigation:** keep explicit degraded status messaging and deterministic bounded query set.
- **Risk:** overly permissive payload parsing could cause false positives.
  - **Mitigation:** only strip fenced wrappers; preserve strict JSON-object contract checks.
- **Risk:** response shape drift from provider.
  - **Mitigation:** tolerant parsing with explicit type/value guards and unit coverage.

## Acceptance Criteria

- `/knowledge` browse no longer appears empty when discoverable wiki content is available through search path fallback.
- Legacy fenced function-call payloads execute correctly for supported tools (e.g., `wiki_write`).
- Existing strict non-legacy rejection behavior remains unchanged.
- Unit tests cover new behavior and pass.

## Validation Sequence

Run from repository root:

```bash
dotnet restore src/LeanKernel.sln
dotnet build src/LeanKernel.sln --no-restore -v minimal
dotnet test src/LeanKernel.sln --no-build -v minimal
scripts/quality/test-coverage.sh
scripts/quality/sonarqube-scan.sh
docker compose build
```
