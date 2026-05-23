# PRD: Response Format Guardrails and Drift Telemetry

## Overview

Prevent non-contextual exam-style artifacts (for example, `The final answer is: $\boxed{0}$`) from reaching users in normal conversations, and add diagnostics that make future model-style drift easy to detect and attribute.

## Problem Statement

A recent production response contained exam-format output in a non-math conversation. Investigation confirmed this text came directly from model output (not token replacement). Current pipeline does not enforce response-shape policy for non-math contexts and lacks enough per-turn telemetry to isolate drift causes quickly.

## Goals

1. Block or normalize exam-style math wrappers in non-math conversations.
2. Preserve valid math formatting when the user clearly asked for math/LaTeX behavior.
3. Emit structured response diagnostics that identify:
   - whether suspicious patterns were present in raw model output,
   - whether normalization occurred,
   - what conversational context was active.

## Non-Goals

1. Building a full semantic response-style classifier.
2. Changing model routing policy or provider selection in this iteration.
3. Storing full raw responses in persistent telemetry.

## Reviewed Plan (Cross-Model Review Applied)

The implementation plan was reviewed with a different model before coding. Incorporated decisions:

1. **Defense-in-depth**: add a response guard enhancer, but also log diagnostics from the raw model response path.
2. **Avoid over-blocking**: use layered math-context signals (query intent + active tool names) rather than only keyword checks.
3. **Safe normalization**: normalize only known high-confidence artifacts (`final answer is` wrappers and `\boxed{...}`) in non-math contexts.
4. **Traceability**: log guard trigger reasons and mutation status.
5. **Edge-case tests**: include code-fence preservation and math-context pass-through tests.

## Functional Requirements

### FR-1 Response Format Guard Enhancer

- Add a final `IResponseEnhancer` that runs last in the enhancer chain.
- For non-math context, detect and normalize:
  - line-leading exam wrappers like `The final answer is:`
  - boxed math output like `\boxed{...}` (with or without `$...$`)
- Preserve fenced code blocks and avoid editing code-fence contents.
- If normalization would produce empty output, return original text unchanged.

### FR-2 Math Context Allowance

- Do not normalize exam/math wrappers when math context is detected.
- Math context signal sources:
  - explicit math intent in user query,
  - active tool names indicating math/calculation behavior.

### FR-3 Drift Telemetry

- Emit per-turn response diagnostics at `Information` level with fields for:
  - raw output pattern flags (`contains_boxed_math`, `contains_exam_wrapper`)
  - final output pattern flags after enhancement
  - mutation status (`response_mutated`)
  - request/session identifiers for correlation
  - active tools summary

## Test Requirements

1. Unit test: non-math response with `The final answer is: $\boxed{0}$` is normalized.
2. Unit test: math-context query preserves boxed output.
3. Unit test: fenced code block containing `\boxed{}` remains unchanged.
4. Unit test: non-matching responses pass through unchanged.

## Rollout and Validation

1. Baseline validation: restore/build/test.
2. Implement enhancer + diagnostics + tests.
3. Post-change validation:
   - `dotnet restore src/LeanKernel.sln`
   - `dotnet build src/LeanKernel.sln --no-restore -v minimal`
   - `dotnet test src/LeanKernel.sln --no-build -v minimal`
   - `scripts/quality/test-coverage.sh`
   - `scripts/quality/sonarqube-scan.sh`
   - `docker compose build`

## Acceptance Criteria

1. Non-math conversations no longer emit exam-style `final answer`/`\boxed{}` wrappers.
2. Math conversations still allow expected math formatting.
3. Response diagnostics clearly indicate raw-pattern detection and mutation status for each turn.
4. All repository quality gates pass.
