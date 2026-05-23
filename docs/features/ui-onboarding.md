# Onboarding UI

## Overview

The Onboarding page is LeanKernel's guided setup flow for durable identity and preference capture. It runs as a five-step Blazor Server wizard and writes the resulting profile into GBrain wiki pages instead of introducing a separate profile store.

This page is related to, but distinct from, the runtime's Phase 2 additive onboarding behavior. The UI gives users an explicit setup experience, while the shared onboarding detector still provides advisory gap guidance for the same identity concepts.

## How It Works

### Wizard flow

The page lives at `/onboarding` and guides the user through five steps:

1. `Welcome`
2. `Identity`
3. `Knowledge Domains`
4. `Goals`
5. `Complete`

The progress rail marks the current step, completed steps, and the final saved state. Knowledge Domains and Goals are optional steps and expose `Skip for now` actions.

### Identity capture

The Identity step collects:

- display name (required)
- role or title
- communication style
- timezone
- preferred language

Communication style is selected from three radio options: Formal, Balanced, and Casual. The page validates display name through `EditForm` and data annotations before the user can continue or save.

### Knowledge domains

The domains step uses a tag-style editor.

- Users can type a domain and press `Enter` or select `Add`.
- Suggested domain chips provide quick-fill options.
- Selected domains appear as removable tags.

### Goals selection

The goals step combines:

- checkbox selection for common goals
- removable selected-goal tags
- a free-text `Other goals` field

This gives the profile both structured goals and room for custom priorities.

### Profile guidance and save behavior

The right sidebar shows advisory `Identity gaps` and recommended next-context guidance. `OnboardingService.DetectGapsAsync` reuses `IOnboardingDetector` and limits the visible guidance to the supported fields used by the wizard.

On save, `OnboardingService.SaveAsync`:

- normalizes the draft values
- serializes frontmatter and markdown body content
- writes only changed pages
- returns partial-save errors when one page write fails

After a successful save, the page shows a completion state with `Go to Chat` and `Review summary` actions.

## Configuration

The page has no standalone UI configuration block, but it depends on shared onboarding and knowledge services:

- the browser owner id is stored in `leankernel.chat.owner-id`, the same local-storage key used by Chat
- `OnboardingService` uses `IKnowledgeService` for page reads and writes
- `IOnboardingDetector` provides gap guidance based on shared identity logic
- shared identity configuration still affects how gap detection behaves, even though this UI writes to fixed wiki pages

## API Endpoints

The Onboarding page does not call dedicated Gateway Minimal API routes today. It uses `OnboardingService` in-process and writes directly through the knowledge service.

Current page keys written by the UI:

- `wiki/identity/user-profile`
- `wiki/identity/user-goals`

## Screenshots / Examples

A user opening `/onboarding` sees:

- a hero header with step count and setup summary text
- a vertical progress rail for the five wizard steps
- identity fields for name, role, tone, timezone, and language
- removable domain tags with suggested chips
- goal checkboxes plus an `Other goals` textarea
- a sidebar with identity-gap guidance and recommended next-context hints
- a completion state that confirms the profile was saved to GBrain wiki pages

## Related documentation

- [Identity and Onboarding](identity-onboarding.md)
- [Scoped Retrieval](scoped-retrieval.md)
- [Knowledge Retrieval](knowledge-retrieval.md)
