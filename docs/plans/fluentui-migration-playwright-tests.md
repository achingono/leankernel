# PRD: Switch UI Framework to Microsoft FluentUI + Playwright Tests

**Date:** 2025-05-24  
**Status:** Approved  
**Reviewed by:** GPT-5.4 (cross-model review)

## Problem Statement

The LeanKernel Gateway UI is currently built with custom HTML/CSS and no component library. The UI is unusable in its current state. Switching to Microsoft FluentUI provides a mature, accessible, Fluent Design-based component library with consistent theming and built-in responsive behavior.

## Target Version

- **Microsoft.FluentUI.AspNetCore.Components v4.14.2** (stable, supports .NET 10)
- **Microsoft.FluentUI.AspNetCore.Components.Icons** (latest stable)

## Scope

### In Scope
1. Replace all custom UI components with FluentUI v4 equivalents
2. Add Playwright-based UI test project (xUnit-based, matching existing test infrastructure)
3. Ensure all existing unit/integration tests continue to pass
4. Docker build remains functional

### Out of Scope
- Backend/API changes
- Authentication changes
- v5 migration (RC/prerelease - not production-ready)

## Implementation Plan

### Phase 1: Dependencies & Configuration

1. Add NuGet packages to `src/LeanKernel.Gateway/LeanKernel.Gateway.csproj`:
   - `Microsoft.FluentUI.AspNetCore.Components` (4.14.2)
   - `Microsoft.FluentUI.AspNetCore.Components.Icons`

2. Register FluentUI services in `Program.cs`:
   - `builder.Services.AddFluentUIComponents();`
   - Add `builder.Services.AddHttpClient();` (required for Blazor Server FluentUI)

3. Update `Components/_Imports.razor`:
   - `@using Microsoft.FluentUI.AspNetCore.Components`

4. Update `Components/App.razor`:
   - Remove custom CSS link
   - Add FluentUI base CSS (`_content/Microsoft.FluentUI.AspNetCore.Components/css/reboot.css`)

### Phase 2: Layout & Navigation

5. Rewrite `Components/Layout/MainLayout.razor`:
   - Use `<FluentLayout>`, `<FluentHeader>`, `<FluentStack>`, `<FluentBodyContent>`
   - Add `<FluentDialogProvider />` and `<FluentMessageBarProvider />`
   - Add `<FluentTooltipProvider />`

6. Rewrite `Components/Layout/NavMenu.razor`:
   - Use `<FluentNavMenu>` with `<FluentNavLink>` items
   - Add FluentUI icons for each navigation item

### Phase 3: Rewrite Pages

7. **Chat.razor**: FluentButton, FluentTextArea, FluentMessageBar, FluentProgressRing
8. **Admin.razor**: FluentCard, FluentBadge, FluentDataGrid, FluentButton
9. **Diagnostics.razor**: FluentTextField, FluentButton, FluentCard, FluentDataGrid
10. **Knowledge.razor**: FluentSearch, FluentSelect, FluentCard, FluentDialog
11. **Onboarding.razor**: FluentWizard/FluentWizardStep, FluentTextField, FluentTextArea, FluentCheckbox

### Phase 4: Shared Components

12. **ChatMessage.razor**: FluentCard, FluentBadge, FluentPersona
13. **SessionList.razor**: FluentNavMenu pattern or FluentCard list

### Phase 5: Cleanup

14. Replace `wwwroot/css/app.css` with minimal overrides (keep layout-specific app styles)
15. Retain `wwwroot/js/chat.js` (preserving element ID references for scroll/keyboard interop)

### Phase 6: Playwright Tests

16. Create `test/LeanKernel.Tests.Playwright/` project:
    - xUnit + `Microsoft.Playwright` (matching existing test framework)
    - Add to solution file
    - Tests target running app at `http://localhost:5080`

17. Test cases:
    - Homepage loads with title "LeanKernel"
    - All nav links navigate correctly
    - Chat page: empty state visible, composer present
    - Admin page: health section renders
    - Diagnostics: session ID input present
    - Knowledge: search field present
    - Onboarding: wizard steps visible

### Phase 7: Validation

18. `dotnet restore && dotnet build && dotnet test` — all tests pass
19. `docker compose build` — container builds successfully
20. Coverage gate: `scripts/quality/test-coverage.sh`

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| FluentUI component markup differs from custom HTML → JS interop breaks | Preserve element IDs used by chat.js |
| Existing integration tests check for specific HTML content | Update assertions to match new FluentUI markup |
| Bundle size increase | FluentUI v4 is optimized; tree-shaking applies to icons |

## Review Notes (GPT-5.4)

- Confirmed v4 (4.14.2) is correct target for production stability
- Must add `FluentDialogProvider`, `FluentMessageBarProvider`, `FluentTooltipProvider` in layout
- Must add `HttpClient` service registration for Blazor Server
- Keep `chat.js` but verify element IDs after migration
- Playwright tests should use role/test-id selectors for resilience
