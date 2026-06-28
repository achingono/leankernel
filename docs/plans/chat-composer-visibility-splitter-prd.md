# PRD: Chat Composer Visibility and Splitter Scroll Containment

## Context

The chat page currently supports multiple sessions and a split layout, but when message history grows the entire right pane can scroll, pushing the composer (text entry) out of view. The desired behavior is a stable chat viewport where:

- the composer is always visible at the bottom of the conversation pane,
- only the message history scrolls,
- the session list (left pane) scrolls independently.

The user provided a Teams-style layout suggestion. We will adapt that structure to the existing LeanKernel UI and Fluent components.

## User Request

1. Import session data from the Docker Swarm database (manager `192.168.1.5`) so local rendering can be validated with many sessions.
2. Keep text entry always available (composer pinned/visible).
3. Adapt the suggested split-pane chat layout for this use case.
4. Validate Fluent component usage via `fluentui-blazor` MCP and validate runtime rendering at `http://localhost:5080`.

## Completed Pre-Implementation Operational Step

Session data import was completed before code changes:

- source: remote swarm postgres (`192.168.1.5`, database `leankernel`)
- imported tables: `engine."Sessions"`, `engine."Turns"`, `engine."CompactionMarkers"`
- target: local docker postgres (`leankernel-database`)
- local chat dataset after import: 16 sessions, 284 turns, 188 compaction markers

## Current Architecture

- Chat page: `src/LeanKernel.Gateway/Components/Pages/Chat.razor`
- Chat scoped styles: `src/LeanKernel.Gateway/Components/Pages/Chat.razor.css`
- Chat JS interop: `src/LeanKernel.Gateway/wwwroot/js/chat.js`
- Shell/global styles: `src/LeanKernel.Gateway/wwwroot/css/app.css`

The page already uses nested `FluentMultiSplitter`:

- horizontal splitter for left sessions vs right conversation pane,
- vertical splitter in conversation pane for messages vs composer.

## Problem Statement

Under realistic data volume, overflow constraints are not strict enough through the layout chain. This allows container-level scrolling in the right pane and can move the composer out of view.

## Goals

- Composer remains visible in the conversation pane at all times.
- Message list is the only vertical scroll area for conversation content.
- Session list scrolls independently from conversation content.
- Layout remains resizable using splitter controls.
- Desktop and mobile continue to function.

## Non-Goals

- Redesigning message bubble styles/branding.
- Replacing Fluent splitters with a custom drag implementation.
- Persisting splitter sizes to storage (unless discovered as necessary for correctness).

## Reviewed Plan (Incorporating Independent Review)

1. Keep the existing nested `FluentMultiSplitter` approach and harden the CSS overflow contract.
2. Ensure each ancestor in the right pane flex/splitter stack has explicit `min-height: 0` and `overflow: hidden` where appropriate.
3. Ensure only `.chat-message-list` has `overflow-y: auto` in the conversation path.
4. Ensure composer pane and composer shell are non-scrolling and always rendered at bottom in right pane.
5. Add a lightweight session search field in the left pane header (adapting user suggestion while preserving existing behavior).
6. Add/adjust Playwright assertions for:
   - composer visibility with overflowing message history,
   - no page-level vertical scroll on chat route,
   - independent scroll containers (left sessions vs right messages).
7. Validate Fluent component guidance with MCP before finalizing component usage.
8. Run build/tests and iterate until quality gates pass.

## Acceptance Criteria

- On `/chat`, with imported multi-session data and long message history:
  - composer input (`#chat-composer-input`) remains visible in viewport,
  - `#chat-message-list` scrolls vertically,
  - page/shell container does not become the effective scroll target for conversation history,
  - left session list remains independently scrollable.
- Splitter resizing still works for:
  - left session pane width,
  - message/composer split in right pane.
- Existing chat send behavior (Enter to send, Shift+Enter newline) remains functional.

## Risks and Mitigations

- Risk: Nested splitter/flex interactions are brittle.
  - Mitigation: enforce explicit overflow/min-height contract on each wrapper.
- Risk: Mobile viewport and keyboard behavior may regress.
  - Mitigation: keep focused mobile media rules and verify with Playwright viewport checks.
- Risk: Large imported data can expose edge-case rendering.
  - Mitigation: validate with imported dataset and run existing chat UI tests.

## Validation Plan

- Fluent MCP:
  - verify component docs for `FluentMultiSplitter` and pane parameters used.
- Runtime:
  - open `http://localhost:5080/chat` and inspect structure/visibility via Playwright snapshot and targeted checks.
- Quality:
  - `dotnet build src/LeanKernel.Gateway/LeanKernel.Gateway.csproj --no-restore -v minimal`
  - relevant tests (unit/integration and chat-focused Playwright as feasible).
