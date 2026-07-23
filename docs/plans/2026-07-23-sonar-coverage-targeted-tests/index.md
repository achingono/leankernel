# Sonar New-Code Coverage Recovery Plan

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Raise SonarQube new-code coverage from 73% to at least 80% by adding targeted tests for high-impact uncovered paths in gateway request/middleware flows and document-ingestion runtime components, then validating with local Sonar scan and quality gate status APIs.

## In Scope
- Add focused unit/integration tests for `DocumentUploadEndpoint`, `AttachmentIngestionMiddleware`, and optionally one additional low-coverage hotspot if needed.
- Reuse existing test conventions and helpers in `test/LeanKernel.Tests.Unit` and `test/LeanKernel.Tests.Integration`.
- Validate with test execution plus `scripts/quality/sonarqube-scan.sh`.
- Record evidence in phase documents.

## Out of Scope
- Functional refactors to production code unless required to enable deterministic testability.
- Relaxing Sonar quality gate thresholds.
- Re-baselining Sonar new-code period.

## Entry Criteria
- Sonar project `LeanKernel` is reachable at local host URL.
- Existing test projects compile and run in the worktree.
- Baseline quality gate failure is confirmed as `new_coverage` below threshold.

## Exit Criteria
_What must be true before this phase closes. See `exit-criteria.md`._

## Roles
- Owner: Coding agent (OpenCode)
- Reviewer: Independent subagent review session
- Approver: Repository maintainer
