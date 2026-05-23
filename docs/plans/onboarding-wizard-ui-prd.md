# Onboarding Wizard UI PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and implementers
- **Review:** Reviewed by `gpt-5.4-mini`. Outcome: proceed after reusing backend onboarding detection instead of UI-only gap logic, defining a stable user key source, keeping wiki writes idempotent, and noting the Gateway-vs-Host documentation discrepancy.

## Problem statement

LeanKernel exposes identity grounding and onboarding-gap logic in backend services, but the Gateway UI still lacks a guided onboarding experience. Users need a focused Blazor Server wizard that explains why identity details matter, captures profile preferences progressively, and writes durable GBrain wiki pages without inventing a separate persistence model.

## Goals

1. Add an interactive onboarding wizard at `/onboarding` in `LeanKernel.Gateway`.
2. Capture identity, knowledge domains, and goals through a responsive multi-step flow.
3. Reopen onboarding with existing values prefilled from GBrain when available.
4. Reuse backend onboarding detection so UI gap feedback matches runtime behavior.
5. Persist profile data into the requested wiki pages:
   - `wiki/identity/user-profile`
   - `wiki/identity/user-goals`
6. Extend navigation and directly related docs for the new user-visible surface.

## Non-goals

- Adding new backend API endpoints.
- Changing `IKnowledgeService` or `LeanKernel.Context` contracts.
- Reworking unrelated Gateway pages or layout structure.
- Adding automated build/test tooling in this environment.

## UX and interaction plan

### Wizard structure

The page will use a single interactive wizard component with five steps:

1. Welcome
2. Identity
3. Knowledge Domains
4. Goals
5. Complete

### Interaction rules

- The stepper shows step number, label, and current-state emphasis with `aria-current="step"` on the active item.
- Identity is required before advancing beyond the identity step; knowledge domains and goals remain skippable.
- The wizard exposes Back and Next buttons throughout the flow, plus Skip actions on optional steps.
- The completion step shows a summary, detected profile gaps, and save confirmation state.
- The page will reuse the existing dark-first visual language, button styles, spacing scale, and responsive behavior patterns already present in `app.css`.

### Accessibility requirements

- Use semantic headings, `EditForm`, visible labels, and fieldset/legend grouping for radio and checkbox collections.
- Provide inline validation/error text near the related field.
- Keep controls keyboard reachable and use descriptive `aria-label` text where necessary.
- Preserve visible focus styles and avoid hover-only affordances.

## Data and service plan

### Stable user identity

The page will reuse the same browser-local owner identifier pattern already used by the chat page so onboarding and chat round-trip against the same user context in unauthenticated local usage.

### Service responsibilities

`OnboardingService` will:

- inject `IKnowledgeService` for page reads/writes;
- inject `IOnboardingDetector` to compute gap feedback from the saved-or-draft identity model;
- load existing `wiki/identity/user-profile` and `wiki/identity/user-goals` pages and map them into a Gateway-specific onboarding model;
- serialize profile/goals content back into deterministic markdown with YAML frontmatter;
- normalize domains and goals case-insensitively to avoid duplicates;
- save both pages in an idempotent sequence and report partial failure clearly.

### Persistence format

`wiki/identity/user-profile` will store YAML frontmatter for:

- `display_name`
- `role`
- `communication_style`
- `timezone`
- `preferred_language`
- `updated_at`
- `user_id`

`wiki/identity/user-goals` will store frontmatter or section metadata needed for parsing plus markdown sections for domains and goals so the page remains human-readable.

### Gap detection

The UI will not invent its own stale/weak-field rules. Instead, `OnboardingService` will map the current onboarding state into an `IdentityContext` shape and call `IOnboardingDetector` so missing-field guidance aligns with backend identity configuration.

## Files to add

- `docs/plans/onboarding-wizard-ui-prd.md`
- `src/LeanKernel.Gateway/Components/Pages/Onboarding.razor`
- `src/LeanKernel.Gateway/Services/OnboardingService.cs`

## Files to update

- `src/LeanKernel.Gateway/Components/Layout/NavMenu.razor`
- `src/LeanKernel.Gateway/Program.cs`
- `src/LeanKernel.Gateway/wwwroot/css/app.css`
- `README.md`
- `docs/plans/index.md`

## Implementation notes

- Keep onboarding-specific parsing and serialization inside Gateway service code rather than depending on internal identity serializer types.
- Reuse the chat page’s browser-storage owner key pattern for local persistence continuity.
- Surface save success and failure with existing banner styles.
- Keep the page mobile-friendly by collapsing the summary and actions into a single-column layout at smaller breakpoints.
- Note in docs that the current implementation lives in `LeanKernel.Gateway`, even though older planning docs still mention `LeanKernel.Host`.

## Validation plan

1. Review the final diff for route wiring, DI registration, and page/service namespace correctness.
2. Inspect the generated markdown formats to confirm they match the requested wiki paths and required frontmatter/body structure.
3. Perform static validation available in this environment (file review, diff inspection).
4. Record that `dotnet` build/test and Sonar validation could not run because the user explicitly stated `dotnet` is unavailable in this environment.

## Acceptance criteria

- `/onboarding` renders an interactive five-step wizard in the Gateway UI.
- The wizard supports progress indication, Back/Next flow, and optional-step skipping.
- Identity fields, domain tags, and goal selections match the requested UI contract.
- Saving creates or updates `wiki/identity/user-profile` and `wiki/identity/user-goals` through `IKnowledgeService`.
- Existing saved onboarding data is loaded back into the wizard when available.
- Gap messaging is derived from backend onboarding detection rather than ad hoc UI-only rules.
- Navigation includes a Setup entry for onboarding.
- README and plan index reflect the new onboarding UI artifact.
