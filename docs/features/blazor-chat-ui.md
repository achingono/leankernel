# Blazor Chat UI

`LeanKernel.Gateway` now serves a foundational Phase 4 Blazor Server interface alongside the existing Minimal API endpoints. The chat UI is the first user-facing page and establishes the layout, navigation, and styling patterns that later diagnostics, admin, and knowledge pages can reuse.

## What shipped

- Interactive Blazor Server app shell rooted at `/`
- Shared layout with persistent navigation for Chat, Diagnostics, Admin, and Knowledge
- Chat workspace with:
  - recent-session rail
  - route-based session resume (`/chat/{sessionId}`)
  - persisted history rendering
  - loading and error states
  - auto-scroll to the newest turn
  - best-effort compaction badges for summarized/compacted history entries
- Placeholder pages for later Phase 4 surfaces so navigation stays stable

## Session model

The current `ISessionStore` only creates or retrieves a session for a `(channelId, userId)` pair. To support multiple browser conversations without changing backend contracts, the Blazor UI creates unique conversation-scoped channel ids in the form `blazor:{conversationKey}` while keeping a browser-scoped owner id stable in local storage.

When persistence is available, the UI reads recent sessions from the existing EF Core store and resumes history through `ISessionStore.GetHistoryAsync`. When persistence is unavailable, the UI keeps the current conversation usable through browser-cached session metadata and the runtime's degraded session handling.

## API coexistence

The existing API endpoints remain mapped before Razor components and continue to serve:

- `POST /api/chat`
- `GET /api/health`
- `GET /api/diagnostics/{sessionId}`
- `GET /api/diagnostics/{sessionId}/context`
- `GET /api/diagnostics/{sessionId}/budget`
- `GET /api/diagnostics/{sessionId}/history`

This means the Gateway can now act as both the automation/API surface and the interactive operator workspace.

## Notes

- Assistant markdown rendering is intentionally basic and safe.
- Compaction badge type is best-effort because persisted compaction markers are not linked back to turns by id.
- The Blazor shell uses a dark-first responsive layout and minimal JavaScript only for local storage, auto-scroll, and Enter-to-send behavior.
