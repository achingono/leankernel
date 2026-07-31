# Phase SonarQube Quality Gate Fix - Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
|---|---|---|---|---|
| R1 | Some uncovered new lines are in auto-generated or excluded files that cannot be tested | May not reach 80% new coverage | Focus on high-impact source files; if short, add tests for additional uncovered paths | Open |
| R2 | EnrichmentQueue tests require SQLite for SQL-raw claim/lock pattern | Test infrastructure dependency | Use SQLite in-memory database (already registered in test project) | Open |
| R3 | JwtSecurityTokenGenerator tests may need System.IdentityModel.Tokens.Jwt | Test project may not have reference | Verify via existing project references; add if missing | Open |
| R4 | SonarQube cache from previous scan may delay new results | Scan results not reflecting new tests | Clear SonarQube analysis cache before re-run | Open |

## Open Decisions
- Need to verify whether the 111 "missing" uncovered lines (168 reported by SonarQube vs 57 found by file-level queries) are in excluded/terminal files
