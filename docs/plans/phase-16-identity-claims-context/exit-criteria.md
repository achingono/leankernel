# Phase 16 Exit Criteria

## Gate Checklist
- [x] Allowlisted OIDC/OAuth claims are persisted to the database as a durable identity profile.
- [x] The identity profile is refreshed on each authenticated resolution (not only at creation).
- [x] A claim allowlist prevents persisting/rendering sensitive or unbounded claims (no tokens).
- [x] The context builder injects a deterministic identity block into the system prompt each turn.
- [x] Identity context is admitted under budget when the Phase 03 gatekeeper is present.
- [x] Identity context is correctly partitioned (no cross-user/tenant leakage).
- [x] Missing claims are handled gracefully without corrupting the prompt.
- [x] Unit + integration tests cover persistence/refresh, allowlist, rendering, budget, and isolation.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | rebuild maintainer | Complete | Implementation delivered in commit `d32bb7d` + prior commits. |
| Reviewer | agent review | Complete | |
| Approver | repository owner | Complete | |
