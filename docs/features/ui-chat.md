# Chat UI

## Overview

The Chat page is LeanKernel's main interactive workspace. It runs as a Blazor Server `InteractiveServer` page on `/`, `/chat`, and `/chat/{sessionId}` and combines session switching, persisted history, composer controls, and lightweight response-state feedback in one surface.

The page is optimized for continuity rather than API-style request entry. It keeps a browser-scoped owner id in local storage, remembers recent session summaries, and reloads persisted conversation history when a session is resumed.

## How It Works

### Session management

- `Chat.razor` initializes `ChatService` after first render.
- The browser stores:
  - `leankernel.chat.owner-id` — the browser-scoped user id reused across sessions
  - `leankernel.chat.sessions` — cached recent session summaries
- `ChatService.InitializeAsync` merges cached sessions with persisted sessions from the database when persistence is available.
- New sessions create a conversation-scoped channel id in the form `blazor:{conversationKey}` and obtain a session id from `ISessionStore`.
- Resuming a session loads turn history through `ISessionStore.GetHistoryAsync` and updates the route to `/chat/{sessionId}`.

### Message display and composer

- The left rail renders recent sessions through `SessionList`.
- The main thread renders user and assistant turns through `ChatMessage`.
- Assistant output uses a deliberately small, safe markdown renderer that supports links, bold text, inline code, and line breaks.
- Best-effort history badges label compacted or summarized messages when matching compaction markers are available.
- The composer binds directly to `ChatService.ComposerText`, allows up to 12,000 characters, and disables send while a turn is running.

### Response flow

1. The user enters a message.
2. The page adds a pending user bubble immediately.
3. `ChatService.SendAsync` calls `IAgentRuntime.RunTurnAsync` with `ui_surface=blazor-chat` metadata.
4. After the turn completes, the page reloads persisted history and refreshes the session list.

The current UI does **not** stream token-by-token assistant output. Instead, it shows a live loading state (`LeanKernel is thinking…`) and replaces the thread with refreshed persisted history when the runtime call finishes.

### Keyboard shortcuts and accessibility

- `Enter` sends the current message.
- `Shift+Enter` inserts a newline.
- The message list uses `aria-live="polite"` so new content is announced without interrupting the page.
- The page uses labels for the composer, `aria-busy` during load/send states, and clear button text for session actions.
- `FocusOnNavigate` in the app router moves focus to the page heading on navigation.

## Configuration

The page has no dedicated UI-only configuration block. Its behavior depends on shared Gateway services:

- session persistence from `ISessionStore` and the database-backed session tables
- runtime execution through `IAgentRuntime`
- optional compaction markers from persistence for compacted and summarized badges

If persistence is unavailable, the chat surface remains usable with browser-cached session summaries and the runtime's resilient session behavior, but recent-session recovery becomes best-effort.

## API Endpoints

The Chat page does not call Gateway Minimal API routes directly. It uses `ChatService`, which invokes `IAgentRuntime` and `ISessionStore` in-process from the Blazor Server circuit.

The existing automation endpoints still coexist with the UI:

- `POST /api/chat`
- `GET /api/health`

## Screenshots / Examples

Without opening DevTools, a user sees:

- a session rail on the left with recent conversations, title previews, and a `New` action
- a centered chat thread with timestamped `You` and `LeanKernel` bubbles
- compacted or summarized history badges when persisted markers can be matched
- a multiline composer with the hint `Enter to send · Shift+Enter for newline`
- a three-dot loading indicator while the turn is being processed

## Related documentation

- [Blazor Chat UI](blazor-chat-ui.md)
- [Gateway API](gateway-api.md)
- [History Shaping](history-shaping.md)
- [Authentication and Authorization](authentication.md)
