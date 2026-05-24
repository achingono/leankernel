# Onboarding FluentUI v4 Markup Migration PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and implementers
- **Review:** Reviewed by `claude-haiku-4.5`. Outcome: proceed with a markup-only migration, prefer FluentCard/FluentBadge step UI over FluentWizard, and verify Fluent form component event/binding compatibility without changing the existing `@code` block.

## Problem statement

`src/LeanKernel.Gateway/Components/Pages/Onboarding.razor` still renders its onboarding experience with custom HTML elements and CSS classes instead of the Microsoft FluentUI v4 components already used across the Gateway. The page should align with the rest of the Gateway UI while preserving all existing onboarding behavior and C# logic.

## Goals

1. Rewrite only the Razor markup in `Onboarding.razor` to FluentUI v4 components.
2. Keep the existing `@code { ... }` section completely unchanged.
3. Preserve the current five-step switch/case onboarding flow and all existing event handlers.
4. Remove custom onboarding CSS classes from the page markup and rely on Fluent components plus inline styles.
5. Keep `EditForm` and `DataAnnotationsValidator` intact for step-one validation behavior.

## Non-goals

- Changing any C# logic in the `@code` block.
- Refactoring onboarding services, models, or validation rules.
- Introducing new onboarding state management abstractions.
- Reworking unrelated Gateway pages.

## UI migration plan

### Top-level shell

- Replace the loading placeholder with a `FluentStack` and `FluentProgressRing` based loading state.
- Replace the page hero with `FluentStack`, `FluentLabel`, and `FluentBadge`.
- Replace error/success banners with `FluentMessageBar`.

### Step navigation

- Keep the existing `CurrentStepIndex`-driven rendering and `switch` statement.
- Replace the custom ordered-list step rail with a `FluentCard`-based vertical summary that uses `FluentBadge` for step numbers and completion state.
- Preserve `aria-current` and busy-state semantics on the surrounding containers where possible.

### Step content

- **Step 0:** Use `FluentCard`, `FluentLabel`, and `FluentBadge` for the welcome summary and saved page keys.
- **Step 1:** Keep `EditForm`, `DataAnnotationsValidator`, and `ValidationMessage`; replace text inputs with `FluentTextField`; replace the radio list with `FluentSelect` and `FluentOption` while keeping the existing communication-style handler.
- **Step 2:** Replace the domain input with `FluentTextField`, use `FluentButton` for add/remove actions, and render selected/suggested domains with `FluentBadge` plus buttons.
- **Step 3:** Replace goal checkboxes with `FluentCheckbox`, keep the existing toggle/remove handlers, and replace the freeform goals area with `FluentTextArea`.
- **Step 4:** Use `FluentCard`, `FluentLabel`, and `FluentBadge` for the review summary.

### Footer and guidance

- Replace navigation buttons with `FluentButton`, preserving handlers and disabled states.
- Show a `FluentProgressRing` next to the save action while `_isSaving` is true.
- Replace the right-side guidance panels with `FluentCard` sections and `FluentLabel` content.

## Compatibility notes

- Prefer FluentUI component patterns already used elsewhere in `LeanKernel.Gateway`.
- Treat form component compatibility as the primary risk area: preserve all current `@bind`, `@onblur`, `@onkeydown`, and `@onchange` behaviors while keeping the `EditContext` flow intact.
- If FluentWizard would force state-model changes, continue using card-based step navigation instead.

## Validation plan

1. Verify the final diff only changes Razor markup above the existing `@code {` section.
2. Run the requested build command from the repository root:
   - `dotnet build src/LeanKernel.Gateway/LeanKernel.Gateway.csproj --no-restore -v minimal 2>&1 | tail -5`
3. If the environment lacks `dotnet`, record that limitation and include the command output.
4. Inspect the rewritten markup to confirm all required handlers, directives, and form validators remain in place.

## Acceptance criteria

- `Onboarding.razor` uses FluentUI v4 components for its rendered page structure.
- The `@code` section remains unchanged.
- The five onboarding switch cases remain intact.
- Existing event handlers and bindings are preserved.
- Error, success, loading, summary, and action states render through FluentUI components.
- Validation status for the required display name field remains represented in the markup.
