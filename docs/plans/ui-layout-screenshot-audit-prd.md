# UI layout screenshot audit PRD

Date: 2026-05-28
Implementation model: GPT-5.5
Plan review: reviewed by Claude Sonnet 4.6. Outcome: proceed with caveats captured below.

## Baseline screenshots

Screenshots were captured from `http://localhost:5080` before this PRD was created:

- `/chat`: `desktop-chat.png`, `mobile-chat.png`
- `/knowledge`: `desktop-knowledge.png`, `mobile-knowledge.png`
- `/diagnostics`: `desktop-diagnostics.png`, `mobile-diagnostics.png`
- `/admin`: `desktop-admin.png`, `mobile-admin.png`
- `/onboarding`: `desktop-onboarding.png`, `mobile-onboarding.png`

Artifacts are stored under `/Users/achingono/.copilot/session-state/cc641312-5675-4516-b687-0d8d2bc43e49/files/ui-screenshots`.

## Problems observed

- Onboarding mobile content is clipped inside Fluent cards; wizard step labels collapse to dot-only navigation and the guidance column stacks below cramped content.
- Admin desktop cards stack down the left side with large unused whitespace; mobile is readable but becomes an endless vertical feed.
- Chat mobile allows the empty-state card and composer to collide, hiding content behind the input area. Desktop proportions are uneven.
- Knowledge mobile clips the fixed-width Browse Pages card and wastes width because the shell/nav and split panel do not adapt well.
- Diagnostics mobile crowds the Session ID field and Load button, clipping placeholder text.

## Root causes

- Page and card surfaces rely on ad-hoc inline flex widths instead of shared responsive primitives.
- Bare Fluent card hosts need explicit box sizing, overflow, and min-width behavior so inner content can wrap instead of crop.
- Data-heavy sections need grid and scroll wrappers, especially on admin tables.
- Mobile shell/nav styles must preserve content width and prevent horizontal overflow.
- Form rows and split panels need mobile stacking rules with full-width controls.

## Plan

1. Add shared layout primitives in `app.css`: card host baseline, auto-fit grid, split panel, responsive table scroller, mobile form stacking, mobile shell/nav fixes, and page max-width/gutter rules.
2. Audit bare `FluentCard` use by retaining a safe global card baseline and applying named classes where layout behavior differs.
3. Update `Onboarding.razor` with semantic two-column wizard/guidance classes, overflow-visible wizard card behavior, and responsive mobile containment. Preserve desktop readability while allowing mobile wizard content to scroll/wrap instead of crop.
4. Update `Admin.razor` so health cards, spend stat cards, spend analytics, and table sections use responsive grids/scroll wrappers instead of a single narrow column.
5. Update `Knowledge.razor` browse/detail split to remove fixed mobile widths and use a responsive split panel.
6. Update `Diagnostics.razor` form and diagnostic split panels to stack full-width on mobile.
7. Update chat layout/CSS only enough to prevent mobile overlap and maintain a readable scroll/composer relationship.

## Acceptance criteria

- No captured page shows clipped FluentCard inner content at desktop 1440x1000 or mobile 390x844.
- Admin desktop uses available horizontal space for card groups and stats instead of a narrow left column.
- Chat mobile keeps the composer below readable content without overlap.
- Knowledge and Diagnostics mobile do not horizontally clip fixed-width cards or form controls.
- Tables remain horizontally scrollable when columns exceed viewport width.
- Touch targets remain at least 44px tall and focus states remain visible.

## Validation

- `dotnet build src/LeanKernel.sln --no-restore -v minimal`
- `dotnet test src/LeanKernel.sln --no-build -v minimal`
- Recapture screenshots from `http://localhost:5080` and inspect desktop/mobile pages.
- `scripts/quality/test-coverage.sh`
- `scripts/quality/sonarqube-scan.sh`

If Sonar cannot run because the local environment lacks a server URL, token, or scanner dependency, document the explicit blocker in the final result.

## Review caveats applied

- Do not remove the global card baseline unless every bare `FluentCard` receives an equivalent class.
- Avoid relying on Fluent shadow DOM internals for control sizing; validate visually through screenshots.
- Preserve existing uncommitted worktree changes and do not revert unrelated edits.
- Keep the route focus target trade-off intentional: `#main-content` is a valid focus destination for the page landmark.
