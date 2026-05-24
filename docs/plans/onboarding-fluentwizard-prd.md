# PRD: Convert Setup Page to FluentWizard

## Context
The Setup page currently renders a custom, card-based stepper flow in `Onboarding.razor`. The user requested migration to built-in Fluent wizard components.

## Goal
Migrate the onboarding setup flow to `FluentWizard` while preserving existing validation, save behavior, guidance, and data semantics.

## Independent Plan Review
Review requested with a different model (`GPT-4.1 (copilot)`) before implementation. Final review notes to be reflected during implementation.

## Reviewed Implementation Plan
1. Replace custom progress step cards with `FluentWizard` and `FluentWizardStep`.
2. Bind wizard step state to `CurrentStepIndex` via `Value`/`ValueChanged`.
3. Preserve all existing step content and business logic.
4. Render custom action buttons in wizard `ButtonTemplate`, reusing existing `GoBackAsync`, `GoNextAsync`, `SkipOptionalStepAsync`, and `SaveAsync` behavior.
5. Keep side guidance cards and success state behavior outside the wizard body.
6. Preserve validation rules for identity step and save guardrails.
7. Add onboarding-scoped CSS for layout polish and responsive behavior.
8. Build and run targeted verification.

## Constraints
- Keep changes feature-local to `LeanKernel.Gateway` onboarding UI.
- Do not alter backend onboarding service contracts.
- Preserve existing onboarding page routes and InteractiveServer mode.

## Rollback
- Revert `Onboarding.razor` and related onboarding CSS changes.
- Rebuild and verify previous setup behavior.

## Acceptance Criteria
- Setup page uses `FluentWizard` for step navigation.
- Existing onboarding fields and save flow still function.
- Step navigation, optional step skipping, and validation continue to work.
- Layout remains responsive on desktop and mobile widths.
