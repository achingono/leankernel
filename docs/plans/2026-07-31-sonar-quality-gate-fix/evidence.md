# Phase SonarQube Quality Gate Fix - Evidence

## Evidence Log

| Item | Reference | Notes |
|---|---|---|
| SonarQube quality gate status | `api/qualitygates/project_status` | Status: ERROR, new_coverage=70.7%, 168 uncovered/648 new lines |
| SonarQube project measures | `api/measures/component` | Alert status: ERROR, bugs=0, coverage=80.3%, code_smells=202 |
| SonarQube quality gate conditions | `api/qualitygates/project_status` | Only new_coverage (70.7% < 80%) fails |
| Per-file new coverage analysis | File-level API queries | 57 uncovered new lines identified across 25+ files |
| Git diff of changed files | `git diff 27c7a72 HEAD` | 92 changed .cs files (including tests) |
| Coverage XML | `coverage-results/sonar/coverage.opencover.xml` | 3 coverage files merged, 597 source files analyzed |
