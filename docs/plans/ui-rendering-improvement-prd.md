# Product Requirements Document (PRD): UI Rendering & Layout Standardization

## 1. Overview

The goal of this initiative is to review, standardize, and improve the UI rendering across the `fluentui-blazor` pages of the LeanKernel Gateway application. Currently, pages such as Admin, Chat, Diagnostics, Knowledge, and Onboarding use inconsistent layout paradigms, custom CSS structures, and varied component nesting. This PRD outlines the requirements to achieve a highly polished, cohesive, and immersive user experience.

## 2. Core Principles & Global Standards

### 2.1 Global Layout Standards

1. **Page Container:** Every page will use `<FluentStack Orientation="Orientation.Vertical" Class="lk-page">` (or `lk-page lk-page-wide` for wider layouts) as the root container to ensure consistent outer margins and maximum widths.
2. **Page Header:** Every page will use a uniform header structure:

   ```html
   <FluentStack Orientation="Orientation.Horizontal" VerticalAlignment="VerticalAlignment.Center" Wrap="true" Class="lk-page-header">
       <FluentStack Orientation="Orientation.Vertical" Class="lk-page-header-copy">
           <h1 class="lk-page-title">Page Title</h1>
           <FluentLabel Typo="Typography.Body" Color="Color.Neutral">Subtitle</FluentLabel>
       </FluentStack>
       <!-- Optional actions/badges here -->
   </FluentStack>
   ```

3. **Card Contents:** All `<FluentCard>` components will wrap their inner content with `<FluentStack Orientation="Orientation.Vertical" Class="lk-card-content">` to ensure consistent internal spacing.
4. **Eliminate Nested Cards:** `FluentCard` elements should only be used as top-level containers for discrete sections. Nested cards create double-borders and visual clutter. Inner grouped content should use standard stacks or `<div class="lk-panel">` for subtle grouping without heavy card shadows.
5. **Icon-Driven Actions:** Common repetitive actions may use icon-only buttons (with `FluentIcon`, `aria-label`, and `Title`) when the icon is immediately recognizable and the interaction also provides a visible tooltip and keyboard-focus treatment. Text labels must remain on primary, destructive, or ambiguous actions, and on any command where the icon alone does not clearly communicate intent.

### 2.2 Premium UI & Performance Polish

1. **Atmospheric Texture:** Inject a subtle SVG/CSS noise overlay onto the global background (`body` or `.lk-shell`) to remove digital sterility and add premium photographic grain.
2. **Hardware Acceleration:** Refactor `.lk-panel` and `.lk-glass-panel` components to exclusively animate compositor-friendly properties (`transform`, `opacity`) instead of `transition: all` to prevent layout thrashing and stutter. Avoid animating `box-shadow` and `border-color` unless you can confirm acceptable performance in the target browsers.
3. **Dimensional Micro-interactions:** Upgrade `.lk-card-interactive` to use `translate3d` and/or `scale()` to give buttons and interactive cards tactile weight and feedback on hover.
4. **FluentUI Blazor Best Practices:** Adhere to `fluentui-blazor` conventions for forms and validation (use the FluentUI equivalents for edit forms and validation messages where available in the repo’s current `fluentui-blazor` version).

---

## 3. Page-Specific Requirements

### 3.1 Admin Console (`Admin.razor`)

- **Objective:** Maintain existing robust structure while eliminating card nesting.
- **Requirements:**
  - Standardize all internal card padding with `lk-card-content`.
  - Remove inner `<FluentCard>` wrappers inside the "Spend tracking dashboard" (e.g., around the chart and budget limit) to let them lay out naturally in the split panel.

### 3.2 Chat Interface (`Chat.razor`)

- **Objective:** Standardize internal typography and layout while preserving full-height scrolling.
- **Requirements:**
  - Replace `.chat-header-row` with the standard `.lk-page-header`.
  - Replace `.chat-heading` with `.lk-page-title`.
  - Maintain the `.chat-page` flex-row layout at the root to ensure chat history scrolls independently of the viewport.

### 3.3 Diagnostics Explorer (`Diagnostics.razor`)

- **Objective:** Fix broken header layout and inconsistent card padding.
- **Requirements:**
  - Wrap the standalone `<h1>` and subtitle in the standard `<FluentStack Class="lk-page-header">`.
  - Add `<FluentStack Class="lk-card-content">` inside all `<FluentCard>` components.
  - Move the "Load a session" empty state to use the unified `.lk-empty-state` container pattern.

### 3.4 Knowledge Base (`Knowledge.razor`)

- **Objective:** Eliminate excessive nesting and standardize spatial design.
- **Requirements:**
  - Remove inner `<FluentCard>` wrappers around "Page content", "Metadata", and "Relationship graph" in the right-hand details pane.
  - Ensure all sub-panels use consistent spacing (`--lk-space-m`).
  - Standardize empty states inside Browse/Search panels to use `.lk-empty-state`.

### 3.5 Onboarding Wizard (`Onboarding.razor`)

- **Objective:** Correct header hierarchy and apply FluentUI component best practices.
- **Requirements:**
  - Extract the page title and subtitle from the `<FluentCard>` and use standard `.lk-page-header` at the top of the `.lk-page`.
  - Replace nested `<FluentCard>` components in the "Complete" step (Identity, Domains, Goals) with `<div class="lk-panel">`.
  - Update `<EditForm>` to `<FluentEditForm>` inside the `<FluentWizardStep>`.
  - Replace standard `<ValidationMessage>` with `<FluentValidationMessage>`.

---

## 4. Implementation Checklist

### Phase 1: CSS Framework & Premium Polish

- [ ] Add SVG/CSS noise overlay to `app.css` (`body` / `.lk-shell`).
- [ ] Refactor `app.css` transitions to use hardware-accelerated properties instead of `transition: all`.
- [ ] Upgrade `.lk-card-interactive` hover states to use `translate3d` and scale.
- [ ] Verify global variables and classes are prepared for `.lk-page-header` and `.lk-card-content`.

### Phase 2: Page Architecture Updates

- [ ] **Admin.razor**
  - [ ] Verify `lk-card-content` usage.
  - [ ] Remove nested `FluentCard` wrappers in Spend Tracking Dashboard.
  - [ ] Replace text buttons (e.g., Export) with icon buttons where appropriate.
- [ ] **Chat.razor**
  - [ ] Replace custom header classes with `.lk-page-header` and `.lk-page-title`.
  - [ ] Replace 'Send' text button with a `FluentIcon` button.
- [ ] **Diagnostics.razor**
  - [ ] Add `.lk-page-header` wrapper to title and subtitle.
  - [ ] Add `.lk-card-content` wrappers to all cards.
  - [ ] Update empty state to use `.lk-empty-state`.
  - [ ] Replace 'Search' text button with icon button.
- [ ] **Knowledge.razor**
  - [ ] Remove nested cards in the details pane.
  - [ ] Standardize empty states.
  - [ ] Replace 'Sync now' / 'New source' buttons with icon-driven actions where appropriate.
- [ ] **Onboarding.razor**
  - [ ] Move header outside of the `FluentCard`.
  - [ ] Replace nested cards in the Complete step with `.lk-panel`.
  - [ ] Update forms to use `FluentEditForm` and `FluentValidationMessage`.

### Phase 3: QA & Verification

- [ ] Verify Chat layout scrolls correctly without breaking the full viewport height.
- [ ] Verify visual hierarchy is consistent across all 5 pages.
- [ ] Verify double borders are eliminated.
- [ ] Add automated Playwright UI tests for the standardized layout and actions.
  - [ ] Evaluate that each page renders a single `.lk-page-header` with a `.lk-page-title` element.
  - [ ] Evaluate that cards use `.lk-card-content` for internal padding (no nested `FluentCard` where the PRD forbids it).
  - [ ] Evaluate icon-only actions expose an accessible name via `aria-label` (or `aria-labelledby`) and have a visible tooltip.
  - [ ] Evaluate keyboard navigation: icon-only actions are reachable via `Tab` and show a `:focus-visible` state.
  - [ ] Evaluate Chat scroll isolation: chat history container scrolls independently while the header remains fixed.
- [ ] Run the application locally (`docker compose up -d --build`) and use Playwright to verify form validation in the Onboarding wizard works.
