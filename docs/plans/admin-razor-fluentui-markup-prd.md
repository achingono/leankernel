# PRD: Admin.razor FluentUI Markup Rewrite

## Overview

Rewrite only the markup portion of `src/LeanKernel.Gateway/Components/Pages/Admin.razor` to use Microsoft FluentUI Blazor v4 components while leaving the entire `@code { ... }` section unchanged.

## Constraints

- Keep `@page`, `@inject`, `@implements`, and `@rendermode` directives unchanged.
- Do not modify any C# logic, helper methods, state, or event-handler methods in the `@code` block.
- Replace HTML headings, info text, buttons, error banner, loading states, cards, badges, select controls, and layout containers with FluentUI components where requested.
- Keep data tables as semantic HTML tables with inline styles because `FluentDataGrid` would require code changes.
- Remove custom CSS classes from the page markup; prefer Fluent component props and inline styles.

## Implementation Plan

1. Inspect the existing page and split the file at `@code {` so only the markup section is rewritten.
2. Replace the page shell and section headers with `FluentStack`, `FluentCard`, `FluentLabel`, and `FluentBadge`.
3. Replace the error banner with `FluentMessageBar` and all loading placeholders with `FluentProgressRing` plus neutral helper text.
4. Keep routing, tool, and jobs data in HTML tables, but restyle them inline and use `FluentBadge`/`FluentSwitch` inside cells where appropriate.
5. Replace the category `<select>` with `FluentSelect`/`FluentOption` using `@bind-Value` and `TOption="string"`.
6. Replace the tool checkbox toggle with `FluentSwitch` using `Value`/`ValueChanged` so the existing `OnToolToggleChangedAsync` handler is still invoked without any C# changes.
7. Replace spend and health cards with `FluentCard` layouts and use `FluentProgress` for budget usage.
8. Run the requested Gateway build command after editing. If `dotnet` is unavailable in the environment, record that verification was attempted but blocked by the missing executable.

## Reviewed Notes

A secondary model reviewed the plan before implementation and flagged three important checks:

- Verify actual FluentUI Blazor v4 APIs before editing, especially `FluentSelect`, `FluentOption`, `FluentSwitch`, and `FluentProgress`.
- Search the repo for removed admin CSS class selectors to ensure they are not used by JavaScript or tests.
- Accept semantic HTML fallbacks with inline styles for structures such as tables and disclosure blocks where Fluent replacements would force logic changes.

## Validation Targets

- `Admin.razor` markup uses FluentUI components per the requested mapping.
- The `@code` section remains byte-for-byte unchanged.
- No custom CSS classes remain in the rewritten markup.
- The requested build command is executed, or the environment limitation is documented if `dotnet` is unavailable.
