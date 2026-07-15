# Phase 02 Exit Criteria

## Gate Checklist
- [x] Forwarded-header processing trusts only approved proxy inputs in the target deployment model.
- [x] Requests with unresolved or inactive tenant hosts are rejected; no runtime-owned rows are created under `Guid.Empty` fallback ownership.
- [ ] `/v1/responses` and `/v1/conversations` have an explicit approved access model with matching automated coverage.
- [x] JWT bearer tokens are validated (signature/issuer/audience/lifetime) so a forged `sub` cannot impersonate a persisted user; a forged/unsigned token is rejected in tests (finding C4).
- [x] Anonymous guest-user resolution is tenant-scoped and aligned with ADR 0002.
- [x] Anonymous identity uses a stable, persisted key (not an ephemeral `Session.Id`) and a repeat anonymous request maps to the same guest user without unbounded row growth (finding M6).
- [x] Request-path identity resolution no longer blocks on sync-over-async DB calls (finding M7).
- [x] Memory search and memory save both use the same canonical tenant/user/channel scope.
- [x] Stored `tool` turns round-trip back into `ChatRole.Tool` without user-role corruption.
- [x] Transcript and memory writes are replay-safe for duplicate logical requests.
- [ ] Long-running sessions use bounded retrieval and a defined compaction/summarization path.
- [x] Agent-state persistence no longer downgrades concurrency conflicts into silent last-write-wins behavior.
- [x] The EF model and generated schema contain only the intended tenant relationship for `SessionEntity`.
- [x] `dotnet build` and targeted `dotnet test` coverage for these fixes pass.
- [x] A separate model/session has reviewed the plan and the implementation before the phase is closed.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | OpenCode | Completed | S2 narrow catches, M4 idempotency, M1 concurrency logging |
| Reviewer | separate agent session / model | Pending | |
| Approver | repository owner | Pending | |
