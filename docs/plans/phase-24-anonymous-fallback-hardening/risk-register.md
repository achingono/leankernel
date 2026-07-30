# Phase 24 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | DevUI fails to authenticate in development | DevUI breaks | Set `AllowGuestFallback: true` in `appsettings.Development.json` | Closed |
| R2 | Existing anonymous tests break silently | False-positive CI pass / false-negative test failure | Explicitly update both unit and integration tests in the same change; verify all pass before merge | Open |
| R3 | Operators unaware of new config flag deploy with guest fallback disabled unexpectedly | 401 errors on existing unauthenticated clients | Add structured warning log when Path C is blocked; document in `docs/configuration/index.md` | Open |
| R4 | Deployments that relied on anonymous fallback in production break on upgrade | Production 401s for unauthenticated flows | Semver-major or feature-flag rollout; document migration path in release notes | Open |

## Resolved Decisions
- **DevUI bearer token migration:** Deferred. The config flag approach (`AllowGuestFallback: true` in development) preserves the existing DevUI session-based flow without requiring a bearer token migration. Token migration can be pursued independently in a later phase.
