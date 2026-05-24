# Test Project Relocation and Coverage PRD

## Context
- Requested change: move all test projects from `src/` to a top-level `test/` folder.
- Quality requirement: all tests must pass and minimum code coverage must be at least 80%.
- Existing risk areas include path-sensitive scripts and Docker restore/build steps.

## Goals
- Relocate all test projects under `test/` without breaking solution build or test execution.
- Keep quality pipeline functioning (unit/integration tests, coverage, Sonar scan).
- Achieve and verify at least 80% code coverage in the existing quality workflow.

## Non-Goals
- Renaming test projects.
- Broad refactors unrelated to relocation or coverage threshold compliance.

## Reviewed Implementation Plan
1. Add `test/Directory.Build.props` to import `../src/Directory.Build.props` so moved projects retain shared build settings.
2. Move test project folders:
   - `src/LeanKernel.Tests.Unit` -> `test/LeanKernel.Tests.Unit`
   - `src/LeanKernel.Tests.Integration` -> `test/LeanKernel.Tests.Integration`
3. Update test project relative `ProjectReference` paths after relocation.
4. Update solution project paths in `src/LeanKernel.sln` to point to moved test project locations.
5. Update path-sensitive pipeline files:
   - `Dockerfile` restore copy lines for test projects.
   - `scripts/quality/test-coverage.sh` runsettings path.
   - `scripts/quality/sonarqube-scan.sh` runsettings path (if referenced).
6. Run validation and quality gates in order:
   - `dotnet restore src/LeanKernel.sln`
   - `dotnet build src/LeanKernel.sln --no-restore -v minimal`
   - `dotnet test src/LeanKernel.sln --no-build -v minimal`
   - `scripts/quality/test-coverage.sh`
   - `scripts/quality/sonarqube-scan.sh`
7. If tests fail or coverage is under 80%, implement focused test/production fixes and re-run quality gates until passing.

## Plan Review (Different Model)
Reviewer: `Explore` subagent (independent review pass).

Key findings incorporated:
- Must update `Dockerfile` copy paths for moved test projects to avoid CI/container restore failures.
- Must update `scripts/quality/test-coverage.sh` and Sonar scan paths to moved runsettings files.
- Must update test `.csproj` relative references after move.
- Suggested ordering improved to catch path/reference failures before expensive quality runs.

Verdict: Approved with required changes.

## Acceptance Criteria
- Test projects physically reside under `test/`.
- `src/LeanKernel.sln` builds and runs tests successfully with relocated projects.
- Coverage workflow completes and reports >= 80% threshold.
- Sonar scan script runs successfully for the changed layout.

## Rollback
- Move test folders back to original `src/` locations.
- Revert solution/script/Dockerfile path changes.
- Re-run baseline restore/build/test workflow.
