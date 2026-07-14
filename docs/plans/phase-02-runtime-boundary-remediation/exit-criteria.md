# Phase 02 Exit Criteria

## Gate Checklist
- [ ] Forwarded-header processing trusts only approved proxy inputs in the target deployment model.
- [ ] Requests with unresolved or inactive tenant hosts are rejected; no runtime-owned rows are created under `Guid.Empty` fallback ownership.
- [ ] `/v1/responses` and `/v1/conversations` have an explicit approved access model with matching automated coverage.
- [ ] JWT bearer tokens are validated (signature/issuer/audience/lifetime) so a forged `sub` cannot impersonate a persisted user; a forged/unsigned token is rejected in tests (finding C4).
- [ ] Anonymous guest-user resolution is tenant-scoped and aligned with ADR 0002.
- [ ] Anonymous identity uses a stable, persisted key (not an ephemeral `Session.Id`) and a repeat anonymous request maps to the same guest user without unbounded row growth (finding M6).
- [ ] Request-path identity resolution no longer blocks on sync-over-async DB calls (finding M7).
- [ ] Memory search and memory save both use the same canonical tenant/user/channel scope.
- [ ] Stored `tool` turns round-trip back into `ChatRole.Tool` without user-role corruption.
- [ ] Transcript and memory writes are replay-safe for duplicate logical requests.
- [ ] Long-running sessions use bounded retrieval and a defined compaction/summarization path.
- [ ] Agent-state persistence no longer downgrades concurrency conflicts into silent last-write-wins behavior.
- [ ] The EF model and generated schema contain only the intended tenant relationship for `SessionEntity`.
- [ ] `dotnet build` and targeted `dotnet test` coverage for these fixes pass.
- [ ] A separate model/session has reviewed the plan and the implementation before the phase is closed.

## Approval Table

| Role | Name | Status | Notes |
| --- | --- | --- | --- |
| Owner | OpenCode | Pending | |
| Reviewer | separate agent session / model | Pending | |
| Approver | repository owner | Pending | |
