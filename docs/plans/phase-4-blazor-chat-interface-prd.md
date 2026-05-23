# PRD: Phase 4 Blazor Chat Interface

## Overview

Implement the foundational Phase 4 web experience in `LeanKernel.Gateway` by adding a Blazor Server chat shell that sits alongside the existing Minimal API endpoints. The goal is to ship a clean, interactive chat workspace that can start new conversations, resume prior conversations, render persisted history, and establish the layout/navigation patterns the rest of the UI can build on.

## Current-State Findings

- `LeanKernel.Gateway` is currently API-only and maps chat, health, and diagnostics endpoints from `Program.cs` and `Endpoints.cs`.
- `IAgentRuntime` exposes `RunTurnAsync(LeanKernelMessage, CancellationToken)` rather than the earlier `ProcessTurnAsync` name.
- `ISessionStore` only supports create-or-get by `(channelId, userId)`, append turn, and history lookup.
- Persisted sessions are unique on `(ChannelId, UserId)`, so multiple UI sessions require unique per-conversation channel identifiers.
- `ConversationTurn` only exposes `IsCompacted` and `CompactionSourceId`; compacted vs summarized marker type is only available separately through persisted compaction markers and cannot be joined perfectly by turn id.
- No Razor components currently exist in the solution.

## Goals

- Add Blazor Server rendering to `LeanKernel.Gateway` without breaking the existing API surface.
- Create the reusable app shell, navigation, and responsive layout for Phase 4 pages.
- Deliver a functional chat page with session rail, message timeline, composer, loading states, auto-scroll, and error handling.
- Resume persisted history through the existing runtime/session abstractions.
- Surface compaction state with best-effort summarized/compacted badges and a safe generic fallback.

## Non-Goals

- Backend contract changes to `IAgentRuntime` or `ISessionStore`.
- WebAssembly, JavaScript frameworks, or streaming transport changes.
- Rich admin/diagnostics/knowledge implementations beyond placeholders and navigation scaffolding.

## Implementation Plan

### 1. Gateway and hosting updates

- Update `src/LeanKernel.Gateway/Program.cs` to register Razor components with interactive server render mode.
- Register a scoped `ChatService` for UI state orchestration.
- Keep existing API mappings intact and map Razor components after API endpoints.
- Add static files and antiforgery middleware for the Blazor UI shell.

### 2. Project and static asset updates

- Update `src/LeanKernel.Gateway/LeanKernel.Gateway.csproj` with the requested Blazor package reference and explicit `wwwroot` content inclusion.
- Add `wwwroot/css/app.css` for the foundational chat layout and theme.
- Add a minimal `wwwroot/js/chat.js` helper for local storage, auto-scroll, and Enter-to-send behavior.
- Add a placeholder `wwwroot/favicon.ico`.

### 3. App shell and navigation

- Add `Components/App.razor`, `Routes.razor`, and `Components/_Imports.razor`.
- Add `Components/Layout/MainLayout.razor` and `NavMenu.razor`.
- Add placeholder pages for `/diagnostics`, `/admin`, and `/knowledge` so the navigation model is real from the start.

### 4. Chat state and session model

- Implement `Services/ChatService.cs` as a thin server-side state layer over `IAgentRuntime`, `ISessionStore`, and optional EF Core persistence access.
- Generate a browser-scoped owner id in the component layer and pass it into `ChatService` so the service stays server-only and JS-free.
- Create a new conversation by generating a unique conversation key and using `channelId = "blazor:{conversationKey}"` with `ISessionStore.GetOrCreateSessionIdAsync(channelId, ownerId)`.
- Load recent sessions from persistence when available by filtering the current owner id and `blazor:` channel prefix; fall back to browser-cached summaries when persistence is unavailable.
- Load conversation history through `ISessionStore.GetHistoryAsync`.

### 5. Chat UI

- Build `Components/Pages/Chat.razor` with:
  - route-based session resume (`/`, `/chat`, `/chat/{sessionId}`)
  - left session rail
  - main message timeline
  - empty state
  - composer with send button
  - loading and error affordances
- Use minimal JS interop from the component to:
  - persist browser owner id and recent session cache in local storage
  - auto-scroll the message list
  - support Enter to send and Shift+Enter for newline

### 6. Shared components

- Add `Components/Shared/SessionList.razor` for recent sessions and new-session action.
- Add `Components/Shared/ChatMessage.razor` for message rendering, timestamps, role-based styling, and safe basic markdown for assistant responses.
- Use best-effort marker matching to label messages as `Summarized` or `Compacted` when persisted compaction marker content matches; otherwise fall back to a generic compacted badge.

### 7. Documentation

- Update `README.md` to note that Gateway now serves both the API and the Blazor chat shell.
- Add Phase 4 feature documentation and update feature indexes accordingly.

## Review Notes Incorporated

This plan was reviewed with a different model before implementation. The review surfaced two important corrections that are incorporated here:

1. **Session creation model:** do not invent a `sessionId` before persistence exists. Instead, create unique conversation-scoped `channelId` values and let `ISessionStore.GetOrCreateSessionIdAsync` create the backing session.
2. **Compaction indicators:** do not rely on a perfect join between history turns and compaction markers. Use best-effort content matching and degrade safely to a generic compacted indicator.

## Validation Plan

- Verify the new UI wiring by code inspection and targeted file diff review.
- Attempt repository validation commands only if `dotnet` is available; otherwise document the environment limitation and avoid false claims of execution.
- Preserve the existing API endpoint mappings and, if practical, extend integration coverage for the UI shell without changing API behavior.
