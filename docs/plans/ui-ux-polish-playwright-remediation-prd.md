# UI/UX Polish and Playwright Remediation PRD

## Overview

Polish the LeanKernel Gateway UI into a consistent, professional, accessible product surface across chat, diagnostics, knowledge, admin, and onboarding. The work is driven by Playwright exploration against the Docker Compose runtime and focuses first on interaction defects that block daily use, then on shared layout, visual hierarchy, responsive behavior, and accessibility consistency.

## Problem statement

The Gateway UI has a functional FluentUI foundation, but Playwright exploration identified interaction and polish gaps that make the experience feel inconsistent:

- Chat composer keyboard input updates the visible Fluent textarea, but the Send button remains disabled during normal keyboard typing and Enter does not submit.
- Many interactive controls render at roughly 32-33px high, below the 44px touch target target used for accessible touch and keyboard ergonomics.
- The mobile chat composer can sit below the visible viewport after focus, making the primary action hard to reach.
- Page headings and route focus management are inconsistent. `FocusOnNavigate` targets `h1`, while most pages use Fluent labels or `h2`.
- Global `FluentCard` glass styling makes all card surfaces visually similar, reducing hierarchy and conflicting with the repository preference to use cards only for true panels/groups.
- Pages contain many inline styles and ad-hoc spacing values, increasing drift across Chat, Knowledge, Admin, Diagnostics, and Onboarding.
- Knowledge empty-state markup contains a likely component typo, using `FluentCard` where text content should use `FluentLabel`.

## Playwright exploration summary

Exploration used the Docker Compose application at `http://localhost:5080`.

Routes audited:

- `/` and `/chat`
- `/diagnostics`
- `/knowledge`
- `/admin`
- `/onboarding`

Viewports audited:

- Desktop: 1440 x 900
- Tablet: 768 x 1024
- Mobile: 390 x 844

Findings:

- No console errors were observed.
- No horizontal overflow was observed at the audited viewports.
- Normal keyboard typing in the chat Fluent textarea leaves `#chat-send-button` disabled and Enter does not send.
- A synthetic composed input event against the Fluent textarea shadow input enables Send, indicating the issue is event/binding propagation rather than service-side validation.
- Navigation links and several Fluent controls are below 44px height at all audited breakpoints.

## Goals

- Make primary chat composition reliable with natural keyboard input, Enter-to-send, and Shift+Enter newline behavior.
- Establish a more consistent enterprise SaaS visual system using shared CSS tokens and semantic layout classes.
- Improve accessibility fundamentals: focus targets, heading hierarchy, touch target sizing, keyboard operation, visible focus, and live status feedback.
- Improve mobile usability without introducing horizontal scroll or hidden primary actions.
- Add Playwright coverage for the critical interaction regressions and responsive/accessibility checks.
- Preserve current data/service behavior while polishing UI structure and presentation.

## Non-goals

- Replacing FluentUI or introducing a new component library.
- Changing backend API contracts, persistence, agent execution, or model routing behavior.
- Implementing a full visual regression platform such as Percy or Chromatic.
- Adding advanced animation systems beyond reduced-motion-safe CSS transitions.
- Reworking all admin data grids into mobile-native cards in this iteration.

## Design principles

- **Accessibility first:** maintain keyboard operability, visible focus, labels, headings, live regions, and contrast-conscious semantic colors.
- **Professional enterprise SaaS:** use restrained surfaces, clear hierarchy, predictable spacing, and consistent action placement.
- **Semantic surfaces:** reserve `FluentCard` and panel styles for true grouping surfaces, not every small UI element.
- **Responsive by default:** use dynamic viewport units, wrapping layouts, and mobile-first behavior for primary actions.
- **Blazor-first interactions:** prefer FluentUI/Blazor binding and events before JS interop. Use JS only for browser-only concerns such as scrolling and shadow-DOM key handling when required.

## Requirements

### Functional requirements

1. Chat composer must enable Send as soon as non-whitespace text is typed through normal keyboard input.
2. Chat composer must submit on Enter when text is non-empty and not loading.
3. Chat composer must preserve newline insertion on Shift+Enter.
4. Chat composer must prevent duplicate submissions while loading and expose loading state through text and ARIA.
5. Route changes must focus a reliable main content target.
6. Each primary page must expose a clear top-level heading.
7. Navigation links and primary actions must have touch-friendly hit areas.
8. Knowledge empty-state typo must be corrected.

### Non-functional requirements

1. No horizontal overflow at 390px, 768px, and 1440px widths.
2. At least the primary navigation and page-level controls must meet a 44px minimum hit area.
3. New CSS must use shared design tokens/classes instead of ad-hoc inline styles where touched.
4. Reduced motion preferences must be respected for newly introduced transitions.
5. Existing service/API behavior must remain unchanged.

## Implementation phases

### Phase 1: Critical interaction fixes

- Add Playwright regression tests for chat natural keyboard typing, Send enablement, Enter submit, and Shift+Enter newline behavior.
- Fix the chat composer using a Blazor-first approach:
  - Add immediate value propagation on `FluentTextArea`.
  - Prefer component key events if reliable.
  - If shadow-DOM key handling is still required, update `chat.js` to bind the internal textarea and notify/click only when appropriate.
- Correct the Knowledge empty-state markup typo.

### Phase 2: UI foundation

- Extend `src/LeanKernel.Gateway/wwwroot/css/app.css` with a clearer design-token section for:
  - spacing
  - radii
  - shadows/elevation
  - panel surfaces
  - touch target minimums
  - focus rings
  - responsive breakpoints
- Replace the global card glass effect with scoped panel/card classes so hierarchy is intentional.
- Add or update layout classes for page shell, main landmark, page headers, panel grids, action rows, status rows, and empty states.
- Add reliable focus target support by aligning `Routes.razor` and page headings/main content.

### Phase 3: Page polish

- Polish Chat:
  - responsive split layout
  - session rail density and active state
  - sticky/mobile-safe composer
  - accessible status feedback
  - empty state without inline style drift
- Polish Knowledge:
  - tab toggle consistency
  - browse/detail layout wrapping
  - consistent empty/loading/error states
- Polish Diagnostics:
  - responsive form and result sections
  - consistent panel spacing
  - readable two-column sections on smaller screens
- Polish Admin:
  - consistent stat cards, data-grid containers, and chart controls
  - touch target improvements for governance controls
- Polish Onboarding:
  - wizard container spacing
  - action row consistency
  - mobile-friendly form grouping

## Acceptance criteria

- Playwright chat regression tests pass against the Docker Compose Gateway URL.
- `#chat-send-button` transitions from disabled to enabled after natural keyboard typing in the composer.
- Pressing Enter in the composer starts the send flow; pressing Shift+Enter inserts a newline and does not send.
- Primary nav links and page-level buttons are at least 44px high at mobile, tablet, and desktop audited widths.
- Playwright audit reports no horizontal overflow for `/`, `/chat`, `/diagnostics`, `/knowledge`, `/admin`, and `/onboarding` at 390px, 768px, and 1440px widths.
- Every primary route has a reliable heading/focus target for route navigation.
- No new console errors appear during Playwright exploration.
- `dotnet restore`, `dotnet build`, `dotnet test`, coverage, Sonar, and `docker compose build` pass.

## Testing strategy

- Use Playwright as the primary UI exploration and regression tool.
- Add targeted Playwright tests before or alongside the chat interaction fix.
- Keep existing unit and integration tests intact.
- Re-run Playwright audit scripts after CSS/layout changes to compare overflow, touch targets, and console errors.
- Capture screenshots in the session workspace during exploration for manual comparison; do not commit generated screenshots unless explicitly required.

## Validation plan

Run from repository root unless otherwise noted:

```bash
dotnet restore src/LeanKernel.sln
dotnet build src/LeanKernel.sln --no-restore -v minimal
dotnet test src/LeanKernel.sln --no-build -v minimal
LEANKERNEL_BASE_URL=http://localhost:5080 dotnet test test/LeanKernel.Tests.Playwright/LeanKernel.Tests.Playwright.csproj -v minimal
scripts/quality/test-coverage.sh
scripts/quality/sonarqube-scan.sh
docker compose --progress plain build
```

Use Docker Compose as the application runtime for UI exploration and final browser validation.

## Rollback plan

The work is phased so a regression can be isolated:

- Revert Phase 1 to restore previous chat composer behavior if interaction changes regress sending.
- Revert scoped CSS/layout changes independently from service logic because the implementation should not change backend contracts.
- Keep generated audit screenshots and scripts in the session workspace only, so rollback does not require cleaning committed artifacts.

## Reviewed plan notes

This PRD incorporates cross-model review feedback:

- Phase implementation to reduce regression risk.
- Prefer Blazor-first interaction fixes before JS shadow-DOM workarounds.
- Add quantitative acceptance criteria for touch targets, overflow, and route focus.
- Add Playwright regression coverage early instead of deferring tests.
- Specify the shared CSS implementation location and rollback approach.
