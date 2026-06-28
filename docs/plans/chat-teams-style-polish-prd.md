# PRD: Teams-Style Chat UI Polish

## Context

The chat page now has splitter-based structure and scroll containment, but the user wants it to visually imitate Microsoft Teams chat more closely while preserving current behavior and architecture.

Requested updates:

1. Far-left app bar collapsed by default.
2. Session list header with icon-only New and Search buttons; search box toggles visibility.
3. Message bubbles align right for user and left for agent, with avatar icons.
4. Composer uses floating tool icons (formatting, emoji, send).
5. Additional visual polish touches.

## Goals

- Keep composer always visible while message list scrolls.
- Apply Teams-inspired visual hierarchy and controls.
- Keep chat page fully functional with imported multi-session data.
- Preserve accessibility and keyboard usability for icon-only controls.

## Non-Goals

- Replacing current navigation architecture.
- Rebuilding message rendering into a new data model.
- Full Teams feature parity (reactions, threads, mentions, etc.).

## Reviewed Implementation Plan

1. Route-scoped app bar collapse:
   - On chat route, reduce `.main-layout-navmenu` width and hide nav text labels.
   - Keep icons and accessible names intact.
2. Session panel header controls:
   - Add icon-only Search/New buttons.
   - Toggle search field visibility from Search button.
   - Keep independent session-list scrolling.
3. Chat bubble refresh:
   - Update `ChatMessage` markup for avatar + metadata rows.
   - User messages right-aligned, agent messages left-aligned.
4. Composer polish:
   - Add compact floating tools row (formatting, attach, emoji, send icon button).
   - Preserve Enter/Shift+Enter and send disabled states.
5. Visual refinements:
   - Improve card/background depth, spacing rhythm, and hover/focus affordances.
6. Tests:
   - Update Playwright selectors/assertions for search toggle and icon-based controls.
   - Validate composer stays visible with long message history.

## Acceptance Criteria

- Chat route shows collapsed left app bar by default (icon rail behavior).
- Session header has icon-only Search and New controls.
- Search field is hidden by default and toggles with Search control.
- User/agent bubble alignment and avatar presentation are clearly distinct.
- Composer tool row is visible and send action remains functional.
- With long history, message list is scroll target and composer remains visible.
- Updated Playwright chat tests pass.

## Risks and Mitigations

- CSS scope leakage into non-chat pages:
  - Scope styling to chat-route containers (`:has(.chat-page)`) and chat classes.
- Test fragility from visual selectors:
  - Use stable selectors/ids and role-based assertions where possible.
- Accessibility regressions from icon-only controls:
  - Ensure `AriaLabel`/`Title` on icon buttons and retain focus-visible states.

## Validation

- Runtime validation at `http://localhost:5080/chat` with imported multi-session dataset.
- `dotnet build` for gateway + Playwright test project.
- Playwright chat suite focused on layout and control behavior.
