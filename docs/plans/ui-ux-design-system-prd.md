# PRD: UI/UX Design System & FluentUI Consistency Audit

**Status:** Approved  
**Date:** 2026-05-24  
**Author:** LeanKernel Engineering  
**Priority:** High  

---

## 1. Executive Summary

This PRD establishes a unified UI/UX design system for LeanKernel's Blazor Gateway, prioritizing native FluentUI Blazor components and eliminating ad-hoc inline styles. The goal is visual consistency across all five screens (Chat, Diagnostics, Knowledge, Admin, Onboarding) through standardized spacing, typography, and component patterns.

---

## 2. UI Audit Findings

### 2.1 Screenshots Captured

Playwright screenshots were captured for all 5 pages at both desktop (1440×900) and mobile (390×844) viewports. Key observations:

### 2.2 Issues Identified

#### A. Inconsistent Spacing & Gap Values
| Location | Current Pattern | Issue |
|----------|----------------|-------|
| Admin.razor | `Style="gap: 1.5rem;"` (root) | Different from other pages using `gap: 1rem` |
| Diagnostics.razor | `Style="gap: 1rem;"` (root) | Consistent with Knowledge |
| Knowledge.razor | `Style="gap: 1rem;"` (root) | Consistent |
| Chat.razor | CSS class `.chat-page { gap: 1rem; }` | Uses isolated CSS instead of inline |
| Onboarding.razor | `Style="gap: 1rem;"` (root) | Consistent |
| Card internal padding | Varies: none/`padding: 0.5rem`/`padding: 1rem`/`padding: 1.5rem`/`padding: 2rem` | Completely inconsistent |
| Section spacing | Mix of `gap: 0.25rem`, `0.5rem`, `0.75rem`, `1rem` | No system |

#### B. Typography Inconsistencies
| Location | Issue |
|----------|-------|
| Chat heading | Uses raw `<h2>` with custom `.chat-heading` class (`font-size: 3rem`) — should use FluentLabel |
| SessionList | Uses raw `<h3>` for "Sessions" title — should use FluentLabel |
| Chat empty state | Uses raw `<h3>` and `<p>` — should use FluentLabel |
| ChatMessage | Uses raw `<div>` for markdown content — no typography wrapper |

#### C. Non-FluentUI Patterns
| Pattern | Location | FluentUI Alternative |
|---------|----------|---------------------|
| Raw `<h2>`, `<h3>` | Chat.razor, SessionList.razor | `FluentLabel Typo="Typography.PageTitle"` / `Typography.Subject"` |
| Raw `<p>` | Chat.razor, SessionList.razor | `FluentLabel Typo="Typography.Body"` |
| Raw `<ul><li>` | Diagnostics.razor | `FluentStack` with items |
| Inline `style=""` for padding/margin | All pages | CSS custom properties / design tokens |
| Custom `.chat-heading` font-size override | Chat.razor.css | FluentLabel with proper Typography enum |
| `border: 2px solid var(--accent-fill-rest)` on cards | SessionList, Knowledge | Use FluentCard with a selected state or custom CSS class |
| `cursor: pointer` on FluentCards | Knowledge.razor | Consider FluentCard interactive pattern or `<FluentAnchor>` |

#### D. Layout Pattern Inconsistencies
| Concern | Detail |
|---------|--------|
| Page header layout | Admin uses full flex space-between with badge; Diagnostics/Knowledge use simpler vertical stack; Chat uses custom CSS |
| Card content padding | Not standardized — varies from 0 to `2rem` |
| Loading states | Identical pattern copy-pasted with slight style differences (`padding: 1rem 0` vs `padding: 2rem`) |
| Empty states | Different centering patterns per page |
| Button placement | Sometimes right-aligned in header, sometimes inline |

#### E. Mobile Responsiveness
| Issue | Detail |
|-------|--------|
| Nav menu | On mobile, nav menu items are truncated ("Diag…", "Know…") |
| Chat sessions panel | Full-width on mobile but sits above main content making chat composer inaccessible without scrolling |
| Admin cards | Flex-based responsive works but min-widths can overflow on very small screens |

---

## 3. Design System Definition

### 3.1 Spacing Scale (Design Tokens)

All spacing should follow the Fluent Design 4px base unit grid:

| Token Name | Value | Usage |
|-----------|-------|-------|
| `--lk-space-xxs` | `2px` | Tight inner margins (badge groups) |
| `--lk-space-xs` | `4px` | Minimal spacing (inline elements) |
| `--lk-space-s` | `8px` | Compact gaps (within cards, between badges) |
| `--lk-space-m` | `12px` | Standard internal card padding |
| `--lk-space-l` | `16px` | Section gaps, card padding |
| `--lk-space-xl` | `24px` | Page-level section spacing |
| `--lk-space-xxl` | `32px` | Major section breaks |
| `--lk-space-xxxl` | `48px` | Hero/empty state vertical centering |

### 3.2 Typography Scale

Use **only** FluentLabel with the Typography enum — no raw HTML headings or paragraphs:

| Hierarchy Level | FluentUI Typo | Weight | Usage |
|----------------|---------------|--------|-------|
| Page title | `Typography.PageTitle` | Bold | One per page, top-level heading |
| Section title | `Typography.Subject` | Bold | Card/section headers |
| Body (default) | `Typography.Body` | Regular | All body text, descriptions |
| Body (emphasis) | `Typography.Body` | Bold | Inline emphasis, stat labels |
| Body (muted) | `Typography.Body` + `Color="Color.Neutral"` | Regular | Hints, timestamps, secondary text |
| Header badge | `Typography.H4` | Bold | App branding only (MainLayout header) |

### 3.3 Color Usage

| Purpose | Token/Approach |
|---------|---------------|
| Primary actions | `Appearance.Accent` buttons |
| Secondary actions | `Appearance.Outline` buttons |
| Status: healthy/success | `Appearance.Accent` badge |
| Status: neutral/info | `Appearance.Neutral` badge |
| Status: warning/degraded | `Appearance.Neutral` badge (amber when available) |
| Status: error | `Appearance.Lightweight` badge or `MessageIntent.Error` |
| Muted text | `Color="Color.Neutral"` on FluentLabel |
| Active/selected borders | `var(--accent-fill-rest)` via CSS class, not inline style |

### 3.4 Component Patterns

#### Page Layout Template
```razor
<FluentStack Orientation="Orientation.Vertical" Class="lk-page">
    <!-- Page Header -->
    <FluentStack Orientation="Orientation.Horizontal" 
                 VerticalAlignment="VerticalAlignment.Center"
                 Class="lk-page-header">
        <FluentStack Orientation="Orientation.Vertical" Class="lk-page-header-copy">
            <FluentLabel Typo="Typography.PageTitle" Weight="FontWeight.Bold">Title</FluentLabel>
            <FluentLabel Typo="Typography.Body" Color="Color.Neutral">Description</FluentLabel>
        </FluentStack>
        <!-- Optional: action badges/buttons -->
    </FluentStack>

    <!-- Page Content -->
    <FluentStack Orientation="Orientation.Vertical" Class="lk-page-body">
        ...
    </FluentStack>
</FluentStack>
```

#### Card Section Template
```razor
<FluentCard Class="lk-card">
    <FluentStack Orientation="Orientation.Vertical" Class="lk-card-content">
        <FluentStack Orientation="Orientation.Horizontal" 
                     VerticalAlignment="VerticalAlignment.Center"
                     Class="lk-card-header">
            <FluentStack Orientation="Orientation.Vertical" Class="lk-card-header-copy">
                <FluentLabel Typo="Typography.Subject" Weight="FontWeight.Bold">Section Title</FluentLabel>
                <FluentLabel Typo="Typography.Body" Color="Color.Neutral">Description text</FluentLabel>
            </FluentStack>
            <!-- Optional: badges/buttons -->
        </FluentStack>
        <!-- Card body content -->
    </FluentStack>
</FluentCard>
```

#### Loading State Template
```razor
<FluentStack Orientation="Orientation.Horizontal" 
             VerticalAlignment="VerticalAlignment.Center" 
             Class="lk-loading">
    <FluentProgressRing />
    <FluentLabel Typo="Typography.Body" Color="Color.Neutral">Loading message…</FluentLabel>
</FluentStack>
```

#### Empty State Template
```razor
<FluentCard Class="lk-empty-state">
    <FluentLabel Typo="Typography.Subject" Weight="FontWeight.Bold">Primary message</FluentLabel>
    <FluentLabel Typo="Typography.Body" Color="Color.Neutral">Supporting description text.</FluentLabel>
</FluentCard>
```

### 3.5 CSS Architecture

Replace all per-page inline `Style=""` attributes with shared CSS classes in `app.css`:

```css
/* ── Design Tokens ─────────────────────────────── */
:root {
    --lk-space-xxs: 2px;
    --lk-space-xs: 4px;
    --lk-space-s: 8px;
    --lk-space-m: 12px;
    --lk-space-l: 16px;
    --lk-space-xl: 24px;
    --lk-space-xxl: 32px;
    --lk-space-xxxl: 48px;
    --lk-radius-s: 4px;
    --lk-radius-m: 8px;
    --lk-radius-l: 12px;
}

/* ── Page Layout ───────────────────────────────── */
.lk-page { gap: var(--lk-space-xl); }
.lk-page-header { justify-content: space-between; gap: var(--lk-space-l); flex-wrap: wrap; }
.lk-page-header-copy { gap: var(--lk-space-xs); }
.lk-page-body { gap: var(--lk-space-l); }

/* ── Cards ─────────────────────────────────────── */
.lk-card { /* FluentCard default, no extra padding needed */ }
.lk-card-content { gap: var(--lk-space-l); }
.lk-card-header { justify-content: space-between; gap: var(--lk-space-l); flex-wrap: wrap; }
.lk-card-header-copy { gap: var(--lk-space-xs); }

/* ── Loading & Empty States ────────────────────── */
.lk-loading { padding: var(--lk-space-xl) 0; gap: var(--lk-space-s); }
.lk-empty-state { max-width: 640px; margin: var(--lk-space-xxl) auto; text-align: center; padding: var(--lk-space-xxl); }

/* ── Interactive Cards ─────────────────────────── */
.lk-card-interactive { cursor: pointer; transition: border-color 0.15s ease; }
.lk-card-interactive:hover { border-color: var(--accent-fill-hover); }
.lk-card-interactive.lk-active { border: 2px solid var(--accent-fill-rest); }
```

---

## 4. Implementation Plan

### Phase 1: Foundation (CSS Design Tokens + Shared Classes)
1. Add design token CSS custom properties to `app.css`
2. Add shared layout utility classes (`.lk-page`, `.lk-card`, `.lk-loading`, etc.)
3. Add FluentProgressRing default sizing class (eliminates inline `width: 24px; height: 24px;`)

### Phase 2: Chat Page Refactoring
1. Replace raw `<h2>`, `<h3>`, `<p>` with FluentLabel
2. Replace `.chat-heading` custom styles with FluentLabel typography
3. Standardize card padding using design token classes
4. Update SessionList.razor to use FluentLabel instead of raw HTML
5. Update ChatMessage.razor typography wrapper

### Phase 3: Diagnostics Page Refactoring
1. Replace inline `Style=""` with design token classes
2. Standardize loading state pattern
3. Replace raw `<ul>` with FluentStack-based list
4. Normalize card padding and section gaps

### Phase 4: Knowledge Page Refactoring
1. Replace inline styles with shared classes
2. Standardize interactive card pattern with `.lk-card-interactive`
3. Normalize browse panel and detail panel layout
4. Standardize empty state pattern

### Phase 5: Admin Page Refactoring
1. Normalize root gap from `1.5rem` → design token `--lk-space-xl`
2. Replace all inline `Style=""` with classes
3. Standardize stat card pattern
4. Standardize section heading pattern

### Phase 6: Onboarding Page Refactoring
1. Replace inline styles with design token classes
2. Standardize wizard step content patterns
3. Normalize card padding within wizard steps
4. Align domain/goal chip patterns with shared classes

### Phase 7: Mobile Optimization
1. Add responsive breakpoint classes for nav truncation fix
2. Ensure chat composer is always visible on mobile
3. Add mobile-specific card padding adjustments

---

## 5. Success Criteria

- [ ] Zero raw HTML elements (`<h2>`, `<h3>`, `<p>`) in page markup — all replaced with FluentLabel
- [ ] All spacing uses CSS custom properties from the design token scale
- [ ] No more than 5 unique inline `Style=""` attributes per page (complex flex layouts only)
- [ ] Loading state pattern is identical across all pages (shared class)
- [ ] Empty state pattern is identical across all pages (shared class)
- [ ] All interactive cards use `.lk-card-interactive` class
- [ ] Build passes, all existing tests pass
- [ ] Playwright visual regression screenshots match improved design

---

## 6. Non-Goals

- Full dark mode implementation (deferred — FluentUI handles via `--base-layer-luminance`)
- Custom icon set (continue using `Microsoft.FluentUI.AspNetCore.Components.Icons`)
- Animation/motion system (defer to FluentUI built-in transitions)
- Complete component library extraction (remain in-app for now)

---

## 7. References

- [Fluent UI Blazor Components](https://www.fluentui-blazor.net/)
- [Fluent Design System Tokens](https://developer.microsoft.com/en-us/fluentui)
- [Microsoft.FluentUI.AspNetCore.Components v4.14.2](https://github.com/microsoft/fluentui-blazor)
