# PRD: Gateway Knowledge Browser UI

## Overview

Build the LeanKernel Gateway knowledge browser as an interactive Blazor Server page that lets users search GBrain knowledge, browse wiki pages, inspect page details, edit/create entries, and navigate linked relationships without leaving the UI.

## Scope

- Rewrite `src/LeanKernel.Gateway/Components/Pages/Knowledge.razor`
- Add `src/LeanKernel.Gateway/Services/KnowledgeUiService.cs`
- Register the UI service in `src/LeanKernel.Gateway/Program.cs`
- Append knowledge browser styles to `src/LeanKernel.Gateway/wwwroot/css/app.css`

## Goals

1. Provide a debounced search experience with result previews and direct page navigation.
2. Provide a browse experience with pagination and sort controls.
3. Provide a detailed page view with metadata, linked pages, and inline editing.
4. Provide a lightweight CSS-only relationship graph for linked pages.
5. Provide a create-page workflow backed by existing knowledge write operations.

## Constraints

- Use `@rendermode InteractiveServer`.
- Use the existing `IKnowledgeService` for core search/get/put operations.
- Do not add external markdown libraries; render content as preformatted text.
- `dotnet` is not available in the environment, so validation must fall back to static inspection and IDE diagnostics.

## Implementation Plan

### 1. UI service

Create `KnowledgeUiService` in `LeanKernel.Gateway.Services` as a UI-focused orchestration layer.

Responsibilities:
- Wrap `IKnowledgeService.SearchAsync`, `GetPageAsync`, and `PutPageAsync`.
- Normalize search results into UI models with slug, score, preview, and content.
- Normalize page detail into slug, content, last modified, tags, and linked pages.
- Attempt richer metadata enrichment through optional `GBrainMcpClient` access for tags and page listing support.
- Attempt a hypothetical `list_pages` MCP tool for browse pagination/sort and degrade gracefully when unavailable.

Design notes:
- Keep the service thin and UI-oriented rather than duplicating knowledge-domain behavior.
- Use primary constructor dependency injection.
- Use XML documentation on public types and methods.
- Use async APIs and `ConfigureAwait(false)` in service methods.

### 2. Browse/list behavior

Implement `BrowsePagesAsync(pageNumber, pageSize, sort, ct)` in the UI service.

Primary path:
- Call `GBrainMcpClient.ListToolsAsync` to detect whether `list_pages` exists.
- If supported, call `list_pages` with normalized pagination/sort arguments.
- Parse common response envelopes such as `items`, `results`, or `pages`.

Fallback path:
- Return a degraded browse response with a clear status message when list support is unavailable or fails.
- Keep the page stable and usable for search/detail/edit/create even without browse data.

### 3. Knowledge page UI

Rewrite `Knowledge.razor` into three coordinated regions:
- Top search panel with debounced search.
- Browse rail with pagination and sort controls.
- Main detail workspace with content, metadata, inline editor, and relationship graph.

State handled in the component:
- Search query, loading state, error state, result list.
- Selected page, page loading state, editor state, save state.
- Browse collection, page number, page size, sort, loading state, degraded state.
- Create-page modal state and form fields.

Behavior details:
- Debounce search by 300ms using a cancellable delay.
- Prevent stale search responses from replacing newer ones by checking a monotonic request version before applying results.
- Use `<pre>` for knowledge content rendering.
- Allow linked pages to be opened from metadata, browse items, search results, and graph nodes.
- Refresh the selected page and browse results after create/edit operations.

### 4. Styling

Append BEM-style knowledge browser styles to `app.css`.

Styling requirements:
- Reuse existing CSS variables and button styles.
- Support empty/loading/error banners and cards.
- Provide responsive layout behavior for desktop and narrow screens.
- Render graph nodes as circular/link cards with simple connector styling.
- Provide accessible modal and form spacing/states.

### 5. Validation

Validation steps:
- Verify `dotnet` availability first.
- If unavailable, use static inspection plus IDE diagnostics on changed files.
- Confirm the implementation covers loading, empty, and error states across search, browse, detail, edit, and create flows.

## Review Summary

Plan reviewed with a different model before implementation.

Review verdict: **approve with changes**

Required adjustments incorporated into this PRD:
- Keep `KnowledgeUiService` thin; do not turn it into a second knowledge adapter.
- Treat `list_pages` as optional and expose a degraded browse state when unsupported.
- Guard against stale debounced search responses with a request-version check.
- Use text/preformatted rendering instead of HTML injection.
- Include accessible modal behavior and explicit per-region empty/error states.
- Use disposal-safe component logic to avoid post-disposal state updates.

## Acceptance Criteria

- Search input debounces by 300ms and displays result slug, relevance score, and preview.
- Clicking a search result loads the page detail view.
- Page detail shows content, slug, last modified, tags, and linked pages.
- Edit mode supports save and cancel without leaving the page.
- Browse rail supports pagination and sort when the backend list tool is available, and shows a clear degraded message when it is not.
- Linked pages are visible in a simple graph panel and are clickable.
- Users can create a new page through the UI using `PutPageAsync`.
- The page uses the existing dark-first design tokens and BEM naming.
