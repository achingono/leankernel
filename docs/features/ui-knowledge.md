# Knowledge UI

## Overview

The Knowledge page is LeanKernel's browser for GBrain-backed wiki content. It combines search, paged browsing, page detail, inline editing, linked-page navigation, and page creation in a single workspace-oriented UI.

The page is intentionally service-backed instead of endpoint-heavy. `KnowledgeUiService` wraps `IKnowledgeService` for reads and writes, then optionally enriches the experience with GBrain MCP tools such as `get_page` and `list_pages` when the connected provider exposes them.

## How It Works

### Search with debounced input

- The page lives at `/knowledge`.
- Typing in the search box starts a 300 ms debounce window.
- After the delay, `KnowledgeUiService.SearchPagesAsync` calls `IKnowledgeService.SearchAsync`.
- Stale search responses are ignored with a request-version check so older results do not overwrite newer input.

Search results show each page slug, provider score, and a trimmed preview. Selecting a result loads that page into the detail workspace.

### Browse and fallback behavior

The left browse rail supports:

- recently modified or alphabetical sort
- paged navigation
- recently changed metadata such as last modified time and tag count

When the connected GBrain provider supports `list_pages`, the browse view uses provider-backed pagination. When it does not (or when listing returns no items), `KnowledgeUiService` runs bounded fallback discovery through `IKnowledgeService.SearchAsync` with seed queries, caches discovered slugs in memory, and then renders that cache as a degraded browse view. The browse list is still best-effort rather than provider-wide, but it is no longer limited only to pages the user already opened in the current browser session.

### Page detail view

Selecting a page loads `KnowledgePageDetail` and renders:

- raw page content
- slug
- last modified time
- tags
- linked page buttons

When GBrain `get_page` is available, `KnowledgeUiService` enriches the base `IKnowledgeService.GetPageAsync` result with additional metadata such as tags, compiled truth, and linked pages.

### Inline page editing and save

The detail workspace switches between read and edit modes.

- `Edit` copies the current content into a textarea.
- `Save changes` writes the updated content through `KnowledgeUiService.SavePageAsync`.
- Saving refreshes the browse list and reloads the selected page so metadata stays in sync.

The current content panel renders page text as raw wiki text inside a `<pre>` block when not editing.

### Relationship graph

The graph view is a simple hub-and-spoke relationship panel rather than a freeform canvas. The selected page is rendered in the center, and each linked page appears as a connected node that can be opened directly.

### Create new page flow

`Create new page` opens a modal dialog with slug and content fields.

- `Create page` writes the page through `IKnowledgeService.PutPageAsync`
- the browse list reloads
- the new page opens immediately in the workspace

This keeps creation, inspection, and editing in one flow.

## Configuration

The page has no dedicated UI-only configuration block. It depends on the configured knowledge provider and the services registered in `Program.cs`.

Important runtime dependencies:

- `IKnowledgeService` for search, read, and write operations
- `GBrainMcpClient` for optional `list_pages` and `get_page` enrichment
- the configured GBrain connection under the normal LeanKernel knowledge settings

If GBrain listing is unavailable, browsing degrades to pages discovered through fallback search and in-session cache state. If metadata enrichment fails, the page still uses the base knowledge-service result.

## API Endpoints

The Knowledge page does not call dedicated Gateway Minimal API routes today. It uses the in-process `KnowledgeUiService`, which in turn calls:

- `IKnowledgeService.SearchAsync`
- `IKnowledgeService.GetPageAsync`
- `IKnowledgeService.PutPageAsync`
- optional GBrain MCP tools such as `list_pages` and `get_page`

## Screenshots / Examples

A user opening `/knowledge` sees:

- a header with a `Create new page` action
- a debounced search panel with score-ranked matches
- a left browse rail with paging and sort controls
- a page workspace with content, metadata, linked pages, and edit actions
- a simple relationship graph with the current page in the center and linked pages around it
- a modal dialog for creating a new page by slug and raw content

## Related documentation

- [Knowledge Retrieval](knowledge-retrieval.md)
- [Scoped Retrieval](scoped-retrieval.md)
- [Identity and Onboarding](identity-onboarding.md)
