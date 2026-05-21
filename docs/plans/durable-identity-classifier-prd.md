# PRD: Durable Identity Classifier Refactor (Generalized, Non-Case-Specific)

## Overview

Replace case-specific transient-detection logic with a generalized, reusable durability classifier so profile sync and prompt sanitation remain resilient across many future scenarios without domain literals.

## Problem

The previous fix used narrow matching that could drift into case-specific behavior. This is brittle and not future-proof.

## Goals

1. Centralize durability detection in one shared classifier.
2. Remove domain-specific literals and use generalized linguistic signals.
3. Reuse the classifier in:
   - `SystemPromptBuilder` line sanitation
   - `UserConfigurationStep` wiki fact sync filtering
4. Increase confidence with focused classifier unit tests.

## Non-Goals

1. Building a full NLP parser.
2. Reworking wiki extraction architecture.
3. Introducing ML classifiers in this iteration.

## Cross-Model Review Outcomes Applied

1. Avoid over-filtering by distinguishing imperative **sentence starts** from descriptive mid-sentence verbs.
2. Expand temporal detection to generalized relative/absolute date-time patterns (not case terms).
3. Use stricter but reasonable length/noise bounds.
4. Add broad-domain test examples (work, religion, health, scheduling) to verify domain independence.

## Functional Requirements

### FR-1 Shared Classifier

- Add a shared classifier (Core layer) with APIs:
  - `IsDurableFact(string value)`
  - `IsTransientInstruction(string value)`
- Ensure regex checks use explicit timeouts.

### FR-2 Generalized Heuristics

Classifier should mark as transient when any applies:

1. Question-like forms.
2. Imperative/task-intent starts (generic verbs).
3. Relative time references (`today`, `tomorrow`, `next week`, `this month`, weekdays with relative framing).
4. Explicit date/time references (ISO, month-day, clock time).
5. Obvious noise or out-of-bounds length.

### FR-3 Integration

- `UserConfigurationStep`: use classifier for profile-fact durability filtering.
- `SystemPromptBuilder`: use classifier for transient line suppression in user-content prompt assembly.

## Testing

1. Add classifier unit tests covering durable vs transient across diverse domains.
2. Keep/update existing sync and prompt-builder tests to ensure behavior remains stable.
3. Verify no domain-specific literals are required for correctness.

## Acceptance Criteria

1. No case/domain-specific keywords are required to suppress one-off tasks.
2. Durable identity/preferences continue to flow into prompt/profile.
3. Transient command-style content is filtered consistently across both ingestion and prompt assembly.
4. Quality gates pass.
