# Product Requirements Document (PRD): UI Enhancement — Chat Experience & Layout Consistency

## 1. Overview

The first UI rendering initiative (`ui-rendering-improvement-prd.md`) established global CSS standards, premium visual polish (noise overlay, hardware-accelerated transitions, card hover effects), and FluentUI component best practices (FluentEditForm, FluentValidationMessage, icon-driven actions, nested card elimination).

This second initiative targets the **Chat experience** (the app's primary user-facing surface) and **cross-page layout consistency**. Key gaps: the session list is a standalone sidebar disconnected from the app's navigation system, Markdown rendering is limited to regex patterns, the message composer scrolls off-screen, and each page uses a slightly different layout shell.

---

## 2. Core Principles

1. **Use existing conventions first** — extend the patterns from the first PRD (`lk-page-header`, `lk-card-content`, `.lk-card-interactive`, icon actions) rather than inventing new layout classes.
2. **No new custom CSS for layout** — prefer FluentUI Blazor layout components (`FluentStack`, `FluentNavGroup`, `FluentCard`) with existing `lk-*` CSS classes. Only add CSS for truly novel visual treatment.
3. **Progressive enhancement** — each change should work standalone; the four workstreams can be implemented in any order.
4. **Chat drives the UX** — the chat page is the primary user surface. All other pages should match its polish level.

---

## 3. Workstream A: Session List → NavMenu Integration

### 3.1 Rationale

The `SessionList` component currently renders inside a `FluentCard` sidebar (`chat-sessions-panel`) with its own "Sessions" heading and "New" button. This duplicates the role of the `FluentNavMenu` in the `FluentMainLayout` and consumes horizontal space that could be used for the conversation. Moving sessions into the navigation menu:
- Frees ~280px of horizontal space for messages
- Aligns with the pattern of sidebar-based navigation common in chat apps (Slack, Discord, ChatGPT)
- Eliminates a redundant component boundary

### 3.2 Requirements

1. **NavMenu.razor** gains a `<FluentNavGroup Title="Sessions">` section after the "Chat" link.
   - The group is collapsible/expandable via `Expanded="false"`.
   - A "New session" action at the group header level (icon button or `FluentNavLink` with a `+` icon).
   - Each session renders as a `FluentNavLink` with `Href="@($"/{session.SessionId}")"`, title text, and optional preview snippet.

2. **`FluentNavGroup`** displays the active session with the `active` styling (matching the current `NavLinkMatch.All` behavior on `/`).

3. **Session data flow**: `NavMenu` currently accepts no parameters. Two approaches:
   - **Preferred (A1): Direct injection.** `NavMenu.razor` injects `ChatService` (singular) to read `Sessions` and `CurrentSessionId`. The `ChatService` is already a singleton-style scoped service. This avoids passing session data through the layout hierarchy and keeps `MainLayout.razor` unchanged.
   - **Alternative (A2): Cascading parameter.** `Chat.razor` sets a `CascadingValue` of type `IReadOnlyList<ChatSessionSummary>`. `NavMenu` receives it via `[CascadingParameter]`. Cleaner DI but requires changes to the layout contract. *Revisit if A1 causes testability issues.*

4. **Remove `chat-sessions-panel`** from `Chat.razor`. The `<FluentCard Class="chat-sessions-panel">` wrapper and its `<SessionList>` child are deleted.

5. **New session routing**: Clicking the "New session" nav action triggers `ChatService.StartNewSessionAsync()`. If NavMenu uses DI (A1), the button calls the service directly. If cascading (A2), it invokes a callback.

6. **Empty state**: When no sessions exist, `FluentNavGroup` shows a placeholder label ("No sessions yet") or collapses entirely with a subtle indicator.

### 3.3 Responsive Behavior

- At ≤900px the nav menu collapses to icon-only. The sessions group also collapses. The "New session" action remains accessible via an icon.
- The `chat-page` layout becomes a single column (it already does at ≤900px via the existing responsive rule).

### 3.4 Files Affected

| File | Change |
|------|--------|
| `Components/Layout/NavMenu.razor` | Add `FluentNavGroup` with session links; inject or cascade session data |
| `Components/Shared/SessionList.razor` | **Remove** — replaced by NavMenu group |
| `Components/Pages/Chat.razor` | Remove `<FluentCard Class="chat-sessions-panel">` and its `<SessionList>` |
| `wwwroot/css/app.css` | **Remove** `.chat-sessions-panel` CSS rules; **Remove** `.lk-session-list`, `.lk-session-item` if unused elsewhere |

---

## 4. Workstream B: Full Markdown Rendering

### 4.1 Rationale

`ChatMessage.razor` currently uses a regex-based `RenderMarkdown()` that only handles bold (`**text**`), inline code (`` `code` ``), and links (`[text](url)`). LLM responses routinely include headings, lists, code blocks, tables, and blockquotes. These render as raw text or broken HTML, severely degrading the chat experience.

### 4.2 Requirements

1. **Add `Markdig` NuGet package** to `src/LeanKernel.Gateway/LeanKernel.Gateway.csproj`.
   - Markdig is the de-facto .NET Markdown processor (used internally by ASP.NET for publishing).
   - Version: latest stable (currently 0.37.0).

2. **Create `Components/Shared/MarkdownSection.razor`**.
   **Parameters:**
   - `[Parameter] public string Content { get; set; } = "";`
   - `[Parameter] public string? Class { get; set; }`
   - `[Parameter] public bool EnableCopy { get; set; } = true;`
   **Behavior:**
   - Processes `Content` through Markdig with a pipeline configured for the application's needs.
   - Renders as `@((MarkupString)html)` inside a `<div>` wrapper.
   - **Html sanitization**: Markdig pipeline uses `DisableHtmlExtensions` to strip raw HTML tags from model output (LLMs can emit `<script>`, `<iframe>`, etc.).
   - **Design tokens**: Rendered HTML uses FluentUI design tokens via CSS classes/scoped styles:
     - Code blocks: `background: var(--neutral-layer-3)`, rounded corners, monospace font.
     - Inline code: same as PRD1 (existing `code` styling in `app.css`).
     - Blockquotes: left border in `var(--accent-fill-rest)` with italic.
     - Tables: full-width, alternating row backgrounds, sticky header.
     - Headings: use the existing `h1-h6` typography overrides in `app.css`.
     - Lists: standard padding with `--lk-space-xxs` gaps.
   - **Copy button** (optional): a `FluentButton` with copy icon appears on hover over code blocks.

3. **Replace the inline render in `ChatMessage.razor`.**
   - The existing:
     ```razor
     <div style="line-height: 1.6; word-break: break-word;">@((MarkupString)RenderMarkdown(Message.Content))</div>
     ```
   - Becomes:
     ```razor
     <MarkdownSection Content="@Message.Content" />
     ```
   - The `RenderMarkdown()` static method and its three regex patterns are deleted.
   - The `@using System.Text.Encodings.Web` and `@using System.Text.RegularExpressions` imports can be removed from `ChatMessage.razor`.

4. **Optional: Extend to other pages.** If Diagnostics or Admin display markdown-like content in the future, `MarkdownSection` can be reused directly without additional work.

### 4.3 Pipeline Configuration

```
var pipeline = new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()     // tables, footnotes, definition lists, task lists, etc.
    .UseEmojiAndSmiley()         // :smile: -> 😄
    .DisableHtml()               // strip <script>, <iframe>, etc.
    .Build();
```

Consider `.UseSyntaxHighlighting()` for fenced code blocks if a theme is available, but this may require additional dependencies. Defer to a follow-up.

### 4.4 Files Affected

| File | Change |
|------|--------|
| `LeanKernel.Gateway.csproj` | Add `Markdig` NuGet reference |
| `Components/Shared/MarkdownSection.razor` | **New** — Markdown processing component |
| `Components/Shared/ChatMessage.razor` | Replace `RenderMarkdown()` call with `<MarkdownSection>`; delete regex methods |
| `wwwroot/css/app.css` | Add code block, blockquote, and table styling using design tokens |

---

## 5. Workstream C: Composer Fixed-to-Bottom

### 5.1 Rationale

The composer (`chat-composer-shell`) is in the normal document flow. As the message list grows, the composer moves below the viewport. The user must scroll all the way down after reading history to type a new message — a fundamental UX failure for a chat application.

### 5.2 Requirements

1. **`chat-page` fills the available height.** Already has `height: 100%` (set in `Chat.razor.css` and `app.css`). Verify it matches the `.body-content` height precisely.

2. **`chat-main` is a flex column with full height.** Already `display: flex; flex-direction: column;` but verify:
   - `flex: 1; min-height: 0;` to fill remaining space.
   - `height: 100%;` already set.

3. **`chat-message-list`** gets:
   - `flex: 1; min-height: 0; overflow-y: auto;` **(already done)** — scrolls independently.

4. **`chat-composer-shell`** gets:
   - `flex-shrink: 0;` — never compressed.
   - Removed from scroll context — stays pinned at the bottom of `chat-main`.
   - No `overflow` that would create its own scrollbar.

5. **Empty state interaction**: When the message list is empty (no messages), the branded empty state card renders **inside** the `chat-message-list`. The composer still sits below it.

6. **"New session" button**: Moves from the header row into the NavMenu (per Workstream A). The `lk-page-header` in the chat page omits the action button (or keeps a minimal "New" button in the header for discoverability; decide during implementation).

7. **Edge case — loading state**: When `ChatService.IsLoading` is true, the `FluentProgressRing` + label is inside `chat-message-list` (as it already is). The composer is disabled but stays visible and pinned.

### 5.3 CSS Changes

**`Chat.razor.css`** (update the `.chat-main` block):
```css
.chat-main {
    flex: 1;
    display: flex;
    flex-direction: column;
    height: 100%;
    min-height: 0;
    gap: var(--lk-space-m);
    overflow: hidden;          /* clips child scroll, only message-list overflows */
}
```

**`app.css`** (update the `.chat-composer-shell` block):
```css
.chat-composer-shell {
    flex-shrink: 0;
    gap: var(--lk-space-xs);
    display: flex;
    flex-direction: column;
    padding: var(--lk-space-xs);
    background: var(--lk-glass-bg);
    border-radius: var(--lk-radius-m);
    border: 1px solid var(--lk-glass-border);
}
```

**`app.css`** (update `.chat-message-list`):
```css
.chat-message-list {
    flex: 1;
    min-height: 0;
    overflow-y: auto;
    padding: var(--lk-space-xs);
    display: flex;
    flex-direction: column;
    gap: var(--lk-space-s);
    background: var(--neutral-layer-2);
    border-radius: var(--lk-radius-m);
    border: 1px solid var(--lk-glass-border);
}
```

### 5.4 Responsive

At ≤900px the `.chat-main` already switches to `overflow: visible` and the `.chat-message-list` to `max-height: none`. The composer's `flex-shrink: 0` isn't relevant on a non-scrolling layout, so the responsive overrides are unchanged.

### 5.5 Files Affected

| File | Change |
|------|--------|
| `Components/Pages/Chat.razor.css` | Update `.chat-main` to `overflow: hidden;` |
| `wwwroot/css/app.css` | Update `.chat-composer-shell` to `flex-shrink: 0;` |
| `wwwroot/css/app.css` | Verify `.chat-message-list` has correct flex properties |

---

## 6. Workstream D: Cross-Page Layout Consistency

### 6.1 Rationale

Each of the five pages (`Chat`, `Admin`, `Diagnostics`, `Knowledge`, `Onboarding`) uses a slightly different layout pattern for headers, card content, and section grouping. These inconsistencies are visible when switching between pages and erode the sense of a polished, cohesive application.

### 6.2 Requirements

#### 6.2.1 Shared `PageHeader` Component

**Create `Components/Shared/PageHeader.razor`:**
```razor
<FluentStack Orientation="Orientation.Horizontal" VerticalAlignment="VerticalAlignment.Center" Wrap="true" Class="lk-page-header">
    <FluentStack Orientation="Orientation.Vertical" Class="lk-page-header-copy">
        <h1 class="lk-page-title">@Title</h1>
        @if (!string.IsNullOrWhiteSpace(Subtitle))
        {
            <FluentLabel Typo="Typography.Body" Color="Color.Neutral">@Subtitle</FluentLabel>
        }
    </FluentStack>
    @Actions
</FluentStack>
```

**Parameters:**
- `[Parameter] public string Title { get; set; } = "";`
- `[Parameter] public string? Subtitle { get; set; }`
- `[Parameter] public RenderFragment? Actions { get; set; }` — slot for buttons, badges, timestamps.

**Adoption across pages:**

| Page | Current header | New usage |
|------|---------------|-----------|
| Chat | Inline `lk-page-header` stack with title + subtitle + "New session" | `<PageHeader Title="Chat" Subtitle="@GetHeaderSubtitle()" />` |
| Admin | Inline `lk-page-header` with title + subtitle + "Live" badge + timestamp | `<PageHeader Title="Admin Console" Subtitle="Govern provider health…">` with `Actions` containing badge + timestamp |
| Diagnostics | Standalone `<h1>` + subtitle + `FluentTextField` section | `<PageHeader Title="Diagnostics" Subtitle="Inspect context…">` |
| Knowledge | Inline `lk-page-header` with title + subtitle + Create/Upload toggle | `<PageHeader Title="Knowledge" Subtitle="Manage wiki pages…">` |
| Onboarding | Title `<h1>` inside card | `<PageHeader Title="Guided setup" Subtitle="Tell LeanKernel…">` |

#### 6.2.2 Shared `EmptyState` Component (Optional)

**Create `Components/Shared/EmptyState.razor`** if two+ pages reuse the pattern. Otherwise keep inline.

Current empty states:
- **Chat**: Rich card with icon + gradient text + description + keyboard hint (branded, intentional — keep as-is or refactor to use `EmptyState`).
- **Diagnostics**: "Load a session to inspect context audit, budget usage, and routing decisions" — plain text, no icon.
- **Knowledge**: "No pages found" / "No documents found" — plain text in the browse panels.

**Decision**: Extract to `EmptyState` component only if the branded Chat empty state can be parameterized. Otherwise standardize the **other** pages to match the `.lk-empty-state` CSS class pattern from PRD1.

#### 6.2.3 Standardized Card Nesting

PRD1 established the convention: `FluentCard > FluentStack.Class("lk-card-content") > (lk-card-header + content)`. All pages should follow this.

- **Admin**: ✅ Already uses `lk-card-content` (fixed in PRD1).
- **Diagnostics**: Already checked in PRD1. Verify.
- **Knowledge**: Uses `lk-card-content` in most places. The right-side details pane uses `lk-card-content` correctly after PRD1.
- **Onboarding**: The wizard steps use a different structure (non-card). OK — this is intentional.
- **Chat**: The message bubbles use `FluentCard` directly without `lk-card-content`. The composition is: `FluentCard > FluentStack (header row) + content`. This can optionally add `lk-card-content` for consistency, but it's not visually broken.

#### 6.2.4 Responsive Audit

- **Chat at ≤900px**: Currently collapses sessions panel to `max-height: 200px`. After Workstream A, the sessions are in the NavMenu, so this rule changes. Update the responsive rule:
  - Remove `.chat-sessions-panel` responsive rule entirely (component is removed).
  - Keep `.chat-page` switching to `flex-direction: column;`.
- **Admin at ≤900px**: The `lk-auto-grid` already collapses. The `lk-split-panel` for spend tracking becomes single column. Verify the tool category filter doesn't overflow.
- **Knowledge at ≤900px**: The `lk-split-panel` for browse/edit becomes single column (handled by existing CSS).
- **Onboarding at ≤900px**: The wizard sidebar and guidance panel should stack. Currently transitions may not be smooth — verify.

#### 6.2.5 "New session" Button Placement

- After Workstream A, the "New session" button lives in the NavMenu as a nav action.
- Optionally, keep a secondary "New session" button in the Chat page header for discoverability (especially for new users who may not look in the collapsed nav).
- **Decision**: Keep a secondary `FluentButton Appearance="Appearance.Outline"` in the Chat page header labeled "New session". This is removed after user onboarding or can remain as a persistent visible action.

### 6.3 Files Affected

| File | Change |
|------|--------|
| `Components/Shared/PageHeader.razor` | **New** — reusable page header component |
| `Components/Shared/EmptyState.razor` | **New** — (optional) reusable empty state component |
| `Components/Pages/Chat.razor` | Replace inline header with `<PageHeader>` |
| `Components/Pages/Admin.razor` | Replace inline header with `<PageHeader>` |
| `Components/Pages/Diagnostics.razor` | Replace inline header/title with `<PageHeader>` |
| `Components/Pages/Knowledge.razor` | Replace inline header with `<PageHeader>` |
| `Components/Pages/Onboarding.razor` | Replace inline header with `<PageHeader>`; move out of card |
| `wwwroot/css/app.css` | Update responsive rules post-session-list-removal |

---

## 7. Implementation Order

```
Phase 1 — Foundation (no visual change risk)
  ├── Workstream D: Create PageHeader component
  └── Workstream B: Add Markdig + MarkdownSection component

Phase 2 — Chat UX (intermediate risk, test each sub-step)
  ├── Workstream C: Composer fixed-to-bottom (CSS-only)
  ├── Workstream A: NavMenu integration (remove sidebar, refactor NavMenu)
  └── Workstream B: Swap ChatMessage.razor to use MarkdownSection

Phase 3 — Cross-page polish (low risk)
  ├── Workstream D: Adopt PageHeader across all 5 pages
  ├── Workstream D: Standardize empty states
  └── Workstream D: Responsive audit & fixes
```

---

## 8. Detailed Validation Checklist

### 8.1 NavMenu Session Integration

- [ ] `NavMenu.razor` injects `ChatService` (or receives cascading parameter) and reads `Sessions` and `CurrentSessionId`
- [ ] Sessions appear as `FluentNavLink` items under a `FluentNavGroup` titled "Sessions"
- [ ] The active session shows the `active` CSS class / highlighted state
- [ ] Clicking a session link navigates to `/{sessionId}` and loads that session in Chat.razor
- [ ] The "New session" action in the NavMenu group triggers `ChatService.StartNewSessionAsync()` and navigates to the new session
- [ ] When no sessions exist, the NavMenu group shows a placeholder (e.g. "No sessions yet") or collapses gracefully
- [ ] The NavMenu group is collapsible/expandable via its built-in toggle
- [ ] After session creation from any page, the NavMenu reflects the change on next render
- [ ] `chat-sessions-panel` FluentCard is removed from Chat.razor — no sidebar visible
- [ ] `SessionList.razor` is deleted (or kept if referenced elsewhere; verify no remaining references)
- [ ] `.chat-sessions-panel`, `.lk-session-list`, `.lk-session-item` CSS rules are removed from `app.css` (or confirmed unused)
- [ ] Chat page layout still renders correctly without the sidebar — no layout shift, no broken gap
- [ ] At ≤900px, the collapsed NavMenu icon still provides access to sessions
- [ ] **Browser test**: Navigate `/` → see NavMenu with session group → click a session → chat loads → back button works
- [ ] **Browser test**: Click "New session" from NavMenu → new session created → empty chat composer ready

### 8.2 Markdown Rendering

- [ ] `Markdig` NuGet package is installed in `LeanKernel.Gateway.csproj`
- [ ] `MarkdownSection.razor` exists in `Components/Shared/` with the documented parameters
- [ ] Markdig pipeline uses `UseAdvancedExtensions()` and `DisableHtml()` (tables, lists, code blocks, blockquotes all supported)
- [ ] Raw HTML in LLM output (e.g. `<script>`) is stripped/escaped — **not** rendered as HTML
- [ ] `ChatMessage.razor` replaces `RenderMarkdown()` call with `<MarkdownSection Content="@Message.Content" />`
- [ ] `RenderMarkdown()` method and its 3 regex patterns are deleted from `ChatMessage.razor`
- [ ] `@using System.Text.Encodings.Web` and `@using System.Text.RegularExpressions` removed from `ChatMessage.razor` (if no longer needed)
- [ ] Rendered Markdown uses FluentUI design tokens:
  - [ ] Code blocks (``` ```) have dark background (`var(--neutral-layer-3)`), monospace font, rounded corners, padding
  - [ ] Inline code (`` ` ``) uses existing `code` styling from `app.css`
  - [ ] Blockquotes (`>`) have a left accent‑colour border and italic text
  - [ ] Tables have alternating row backgrounds and sticky headers
  - [ ] Headings (`#` … `######`) match the existing `h1‑h6` typography overrides
  - [ ] Lists (`-`, `1.`) have proper indentation and spacing
- [ ] Long code blocks have a horizontal scrollbar (not overflowing the card)
- [ ] **Browser test**: Send `# Heading\n**bold**\n- list item\n\`code\`\n```\nblock\n```\n> quote\n| a | b |` as a user message — all render correctly
- [ ] **Browser test**: Agent response with a fenced code block, table, and blockquote renders correctly
- [ ] **Browser test**: Copy button appears on hover over code blocks (if enabled)
- [ ] **Browser test**: No JavaScript errors in console when rendering complex markdown

### 8.3 Composer Fixed-to-Bottom

- [ ] `.chat-main` has CSS `display: flex; flex-direction: column; height: 100%; min-height: 0; overflow: hidden;`
- [ ] `.chat-message-list` has CSS `flex: 1; min-height: 0; overflow-y: auto;` — scrolls independently
- [ ] `.chat-composer-shell` has CSS `flex-shrink: 0;` — never compressed
- [ ] After loading a session with 20+ messages, the composer remains visible at the bottom of the viewport
- [ ] Scrolling the message list does not move the composer
- [ ] When the message list is empty, the branded empty state renders above the composer
- [ ] When `IsLoading` is true, the progress indicator renders in the message list area, composer stays visible but disabled
- [ ] Resizing the browser window (narrow/tall) does not break the layout
- [ ] **Browser test**: Create new session → no messages → composer is at bottom. Send a message → message appears above composer. Send 15 messages → scroll message list → composer stays pinned. Verify with devtools that `chat-composer-shell` does not have its own scrollbar.
- [ ] **Browser test**: Open a session with long history → scroll up → click "Send" button → message is appended → list auto-scrolls to bottom → composer still visible

### 8.4 Cross-Page Layout Consistency

#### PageHeader Component
- [ ] `PageHeader.razor` renders `.lk-page-header > .lk-page-header-copy > h1.lk-page-title + optional subtitle`
- [ ] `Actions` renderFragment renders at the right side of the header
- [ ] Chat page uses `<PageHeader Title="Chat" Subtitle="@GetHeaderSubtitle()" />`
- [ ] Admin page uses `<PageHeader Title="Admin Console" Subtitle="Govern provider health…">` with `Actions` containing "Live" badge and timestamp
- [ ] Diagnostics page uses `<PageHeader Title="Diagnostics" Subtitle="Inspect context…">` (no longer has the `<h1>` outside a header wrapper)
- [ ] Knowledge page uses `<PageHeader Title="Knowledge" Subtitle="Manage wiki pages…">` with tab-toggle in `Actions`
- [ ] Onboarding page uses `<PageHeader Title="Guided setup" Subtitle="Tell LeanKernel…">` (moved out of the `FluentCard`)
- [ ] **Browser test**: Navigate through all 5 pages — each shows a consistent `.lk-page-header` with `.lk-page-title` at the top

#### Empty States
- [ ] Chat empty state is reviewed: keep the branded card, or optionally extract to shared `EmptyState` component
- [ ] Diagnostics "Load a session" empty state uses `.lk-empty-state` class (from PRD1)
- [ ] Knowledge browse/search empty states use `.lk-empty-state` class (from PRD1)

#### Card Nesting
- [ ] All `FluentCard` components in Admin, Diagnostics, Knowledge wrap content in `<FluentStack Class="lk-card-content">`
- [ ] No nested `FluentCard` elements remain (per PRD1 requirement; re-verify)
- [ ] Chat message bubbles optionally use `lk-card-content` for internal padding consistency

#### Responsive
- [ ] Chat at ≤900px: no `.chat-sessions-panel` responsive rule (component removed); `.chat-page` switches to `flex-direction: column; height: auto;`
- [ ] Admin at ≤900px: tool category filter does not overflow; tables scroll horizontally
- [ ] Knowledge at ≤900px: split panel becomes single column; tab-toggle wraps correctly
- [ ] Onboarding at ≤900px: wizard sidebar + guidance stack vertically
- [ ] **Browser test**: Resize browser to 800px wide → navigate through all 5 pages → verify no horizontal scrollbar, no overlapping elements, no broken layout
- [ ] **Browser test**: Resize browser to 400px wide (mobile) → verify chat page composer fills width, messages don't overflow

### 8.5 Regressions

- [ ] Existing tests still pass (if any)
- [ ] No new console errors/warnings introduced
- [ ] All existing keyboard navigation patterns work (Tab, Enter, Escape)
- [ ] The `.csproj` change (Markdig dependency) does not break `dotnet build`
- [ ] The `Dockerfile` builds without error (NuGet restore picks up Markdig)
- [ ] Chat page still loads with no session selected (empty state renders)
- [ ] Chat page still loads with a session ID in the URL (session loads and renders)
- [ ] Chat page still creates new sessions via the "New session" button in the header (secondary button)
- [ ] OAuth2-proxy flow is unaffected (all changes are client-side Blazor)

### 8.6 Final Integration Test

- [ ] `dotnet build` succeeds with no warnings related to the changes
- [ ] `npm run test` (or equivalent test command) passes in the leankernel repo
- [ ] Deploy to swarm via `deploy/leankernel/scripts/deploy.sh --build`
- [ ] All 5 leankernel services converge to 1/1
- [ ] Manual walkthrough:
  1. Navigate to `https://app.leankernel.com/` → Chat page loads with no outer scrollbar (PRD1 fix)
  2. Session list visible in NavMenu group, composer fixed at bottom
  3. Send a message → Markdown renders correctly
  4. Click "Diagnostics" → header matches Chat header layout
  5. Click "Admin" → header matches, cards use consistent padding
  6. Click "Knowledge" → header matches, cards use consistent padding
  7. Click "Setup" → header matches, no card-wrapped header
  8. Resize to 800px → all pages responsive, no broken layout
  9. Hard refresh on each page → no FOUC or layout shift
