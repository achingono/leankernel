# Phase SonarQube Quality Gate Fix - Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| SonarQube quality gate status for LeanKernel | `api/qualitygates/project_status?projectKey=LeanKernel` | Build Agent |
| New code coverage metrics (new_coverage, new_uncovered_lines, new_lines_to_cover) | `api/measures/component` | Build Agent |
| Per-file new coverage data | `api/measures/component?component=LeanKernel:<path>` | Build Agent |
| Git diff of changed files (27c7a72..HEAD) | Local git repo | Build Agent |
| Coverage XML (OpenCover format) | `coverage-results/sonar/` | Build Agent |
| Source files with uncovered new lines | Repo source tree | Build Agent |
| Existing test patterns | `test/LeanKernel.Tests.Unit/` | Build Agent |

## Optional Inputs
- None

## Input Validation Checklist
- [x] All required inputs are current (sonar scan run on 2026-07-31)
- [x] No required input is missing or in draft state
