# Phase 16 Exit Criteria

## Gate Checklist
- [ ] Allowlisted OIDC/OAuth claims are persisted to the database as a durable identity profile.
- [ ] The identity profile is refreshed on each authenticated resolution (not only at creation).
- [ ] A claim allowlist prevents persisting/rendering sensitive or unbounded claims (no tokens).
- [ ] The context builder injects a deterministic identity block into the system prompt each turn.
- [ ] Identity context is admitted under budget when the Phase 03 gatekeeper is present.
- [ ] Identity context is correctly partitioned (no cross-user/tenant leakage).
- [ ] Missing claims are handled gracefully without corrupting the prompt.
- [ ] Unit + integration tests cover persistence/refresh, allowlist, rendering, budget, and isolation.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | | Pending | |
| Reviewer | | Pending | |
| Approver | | Pending | |
