# Knowledge FluentUI Markup PRD

## Summary
Rewrite the markup section of `src/LeanKernel.Gateway/Components/Pages/Knowledge.razor` to use Microsoft FluentUI v4 components while keeping the `@code { ... }` block completely unchanged.

## Constraints
- Keep `@page`, `@inject`, `@implements`, and `@rendermode` directives unchanged.
- Keep all C# logic in the `@code` block byte-for-byte unchanged.
- Replace existing HTML and CSS-class-based markup with FluentUI components and inline styles.
- Preserve all existing bindings, conditionals, loops, ids, and event handlers.
- Verify with `dotnet build src/LeanKernel.Gateway/LeanKernel.Gateway.csproj --no-restore -v minimal 2>&1 | tail -5` from repo root.

## Existing FluentUI Context
- `src/LeanKernel.Gateway/Components/_Imports.razor` already imports `Microsoft.FluentUI.AspNetCore.Components`.
- `src/LeanKernel.Gateway/Program.cs` already registers FluentUI via `AddFluentUIComponents()`.
- Existing pages such as `Chat.razor`, `Diagnostics.razor`, and `Shared/SessionList.razor` establish local FluentUI patterns for `FluentStack`, `FluentCard`, `FluentMessageBar`, `FluentTextField`, `FluentTextArea`, `FluentBadge`, and `FluentProgressRing`.

## Reviewed Implementation Plan
1. Read the full `Knowledge.razor` markup and the split point at `@code {`, plus inspect existing Gateway FluentUI pages for component syntax.
2. Replace only the pre-`@code` markup with a FluentUI layout using `FluentStack` for structure, `FluentCard` for panels, `FluentLabel` for headings/body text, `FluentSearch` or `FluentTextField` for search, `FluentSelect`/`FluentOption` for sort, `FluentMessageBar` for errors, `FluentProgressRing` for loading, `FluentBadge` for tags and meta chips, `FluentTextArea` for content editing, and `FluentDialog` with `@bind-Hidden` for the create modal.
3. Preserve all current conditionals, displayed text, aria-relevant behavior, ids, event handlers, and disabled states while removing custom CSS classes in favor of inline styles and FluentUI props.
4. Write the complete file back while leaving the `@code` section unchanged.
5. Validate with the requested build command and report environmental blockers if the `dotnet` CLI is unavailable.

## Review Notes Applied
- Do not add new imports or registrations because FluentUI is already configured in `_Imports.razor` and `Program.cs`.
- Pay close attention to FluentUI binding syntax (`@bind-Value`, `Value`, `SelectedOption`, `@onchange`, etc.) so existing interactions remain intact.
- Preserve accessibility-relevant labeling and dialog semantics while relying on FluentUI components where possible.
- Prefer component props like `Appearance` over styling where available, and only use inline styles for layout/spacing.

## Acceptance Criteria
- `Knowledge.razor` markup uses FluentUI v4 components instead of raw HTML controls and status banners.
- The `@code` block is unchanged.
- The page still preserves search, browse, edit, navigation, and create dialog interactions.
- The requested project build command is executed and its result is reported.
