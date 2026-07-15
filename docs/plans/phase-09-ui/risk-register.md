# Phase 09 Risk Register

## Risks

| ID | Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- | --- |
| R1 | UI leaks cross-tenant/user data in lists/search/diagnostics | Privacy breach | Enforce partitioning in every query; isolation e2e tests | Open |
| R2 | UI services reimplement runtime logic, causing drift | Inconsistent behavior | Keep services thin; call existing runtime/gateway services | Open |
| R3 | Blazor Server scaling/connection issues under load | Poor UX | Circuit management + load testing | Open |
| R4 | UI hard-fails when a backend (diagnostics/GBrain) is down | Broken pages | Graceful degradation + empty/error states | Open |
| R5 | Flaky Playwright tests | CI instability | Deterministic selectors + stable test data | Open |

## Open Decisions
- Component/design-system choice and whether a design-system migration is in-phase or a follow-up.
- Sequencing: ship chat + knowledge first, then diagnostics/admin as their APIs land.
