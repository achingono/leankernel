# PRD: SonarQube Scan Job Fix (Summary Script Permission)

## Problem
The GitHub Actions job **SonarQube Scan** failed in run `27317925175` (job `80702489386`) even though Sonar analysis and quality gate passed.

## Root Cause
The workflow executes `scripts/quality/sonarqube-summary.sh` directly. In the repository, that script is not executable (`-rw-rw-r--`), so the step fails with:

`Permission denied` (exit code `126`).

## Minimal Fix
Update the workflow step to invoke the script through Bash:

- from: `run: scripts/quality/sonarqube-summary.sh`
- to: `run: bash scripts/quality/sonarqube-summary.sh`

This removes dependency on file mode while keeping behavior unchanged.

## Review Notes (Cross-model)
A separate model reviewed the plan and confirmed it is sufficient, with emphasis on proving root cause from logs and keeping the workflow fix minimal.

## Validation Plan
1. Baseline repo validation before changes (`restore`, `build`, `test`) to establish pre-existing issues.
2. Run `bash scripts/quality/sonarqube-summary.sh` locally to verify no permission failure.
3. Run shell syntax check on the summary script.
4. Re-run targeted workflow-related validation after the change.
5. Run secret scan on changed files.

## Rollback
Revert the workflow line to direct execution if any regression is observed.
