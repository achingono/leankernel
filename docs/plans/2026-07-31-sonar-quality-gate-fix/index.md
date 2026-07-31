# Phase SonarQube Quality Gate Fix

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Fix the SonarQube quality gate failure for the `LeanKernel` project by adding targeted unit tests to raise `new_coverage` from 70.7% to >= 80%. The only failing condition in the "Sonar way" quality gate is `new_coverage < 80` (actual: 70.7%). 168 new code lines are uncovered out of 648 total.

## Scope

### In Scope
- Add unit tests for `JwtSecurityTokenGenerator` (0% coverage, 17 uncovered new lines)
- Add unit tests for `ChannelConfigurationValidatorHostedService` (28.6% coverage, 2 uncovered new lines)
- Add unit tests for `EnrichmentQueue.TryClaimSuccessorAsync` SQL claim path (33.3% coverage, 4 uncovered new lines)
- Add tests for uncovered paths in `FileCopyTool` (33.3% coverage, 6 uncovered new lines)
- Add tests for uncovered paths in `IdentityResolver` (36.4% new coverage, 4 uncovered new lines)
- Add tests for uncovered paths in `TextExtractionHelper` (83.7% new coverage, 2 uncovered new lines)
- Add tests for uncovered paths in `DiagnosticsCleanupHostedService` (82.8% new coverage, 4 uncovered new lines)
- Add tests for uncovered paths in `GBrainDreamService` (60% new coverage, 1 uncovered new line)
- Add tests for uncovered paths in `IEndpointRouteBuilderExtensions` (83.3% new coverage, 1 uncovered new line)
- Add tests for uncovered paths in `PromptAssembler` (50% new coverage, 1 uncovered new line)

### Out of Scope
- Resolving code smell issues (CA1873, CA1859, etc.) - these do not affect the quality gate
- The 2 CRITICAL maintainability issues in health check parameter names (does not fail the gate)
- Signal and Teams terminals (excluded from SonarQube analysis)

## Entry Criteria
- SonarQube running and healthy at localhost:9000
- Quality gate project status is ERROR with new_coverage = 70.7%
- New code period baseline: commit 27c7a72 (2026-07-30T17:16:29+0000 UTC)

## Exit Criteria
- `new_coverage` >= 80% on the LeanKernel SonarQube project
- Quality gate status = OK
