## Deep Review Findings (after SonarQube)

Run: deep-review sub-agent

### Critical

**File/Module:** `src/Common/LeanKernel.Logic/Tools/ToolGovernancePolicy.cs`, `src/Common/LeanKernel.Logic/Extensions/IServiceCollectionExtensions.cs:175`, `src/Services/LeanKernel.Gateway/Programs.cs:206`

**The Issue:** The current tool control plane is an allowlist-at-registration model only; there is no runtime autonomy/approval interception or durable action audit path in the execution pipeline, despite Phase 14’s trust-boundary intent. Write-capable tools can run once exposed to the model.

**Why Static Analysis Missed It:** Static analyzers see valid DI wiring and policy filtering, but cannot infer missing governance workflow semantics (classify → require approval → audit) across runtime boundaries.

**Impact:** Side-effecting actions can execute without explicit human confirmation or tamper-evident audit, which is a direct trust-boundary gap for connector/file/data write paths.

**Recommended Fix:** Introduce a single runtime enforcement hook around tool invocation that performs risk classification, per-tenant/person policy evaluation, approval gating, and append-only audit persistence; fail closed when approval/audit services are unavailable.

### Major

**File/Module:** `src/Common/LeanKernel.Core/Entities/TurnEntity.cs:95`, `src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs:115`, `src/Common/LeanKernel.Data/EntityContext.cs:119`

**The Issue:** Retry dedup is content+role+5-minute-bucket based, which collapses legitimate repeated turns within a bucket, while still being race-prone under concurrent retries because dedup is in-memory (`existingKeys`) with no DB uniqueness on the dedup key.

**Why Static Analysis Missed It:** Each query and hash function is locally valid; the failure mode appears only when combining user behavior (repeated short messages) with distributed retry/concurrency timing.

**Impact:** Transcript correctness drifts (real turns dropped), and under parallel retries duplicates can still be inserted; both degrade downstream memory extraction and telemetry attribution.

**Recommended Fix:** Persist a transport/request-scoped operation id (or request_id) and enforce uniqueness at DB level (unique index per session + operation); keep content-hash only as legacy fallback, not as primary idempotency key.

**File/Module:** `src/Terminals/LeanKernel.Channels.Teams/TerminalService.cs:32`, `src/Terminals/LeanKernel.Channels.Teams/Clients/GatewayClient.cs:24`, `src/Terminals/LeanKernel.Channels.Signal/TerminalService.cs:48`, `src/Terminals/LeanKernel.Channels.Signal/Clients/GatewayChannelClient.cs:28`

**The Issue:** Channel-native delivery ids are not propagated to gateway requests (no idempotency field), so at-least-once redelivery/retry from channel transports cannot be distinguished from new turns at persistence boundaries.

**Why Static Analysis Missed It:** There is no syntax/API error; this is a cross-module distributed-systems contract gap between terminal ingress and gateway persistence.

**Impact:** Duplicate model turns and duplicate memory writes under normal network retries/webhook redelivery; conversely, dedup workarounds based on message text can suppress legitimate repeated user intent.

**Recommended Fix:** Carry channel message identity (`ActivityId`/Signal message id) into `/v1/responses` as a stable operation key and thread it through chat-history + memory persistence as the dedup authority.

**File/Module:** `src/Services/LeanKernel.Gateway/Providers/TenantResolutionMiddleware.cs:86`, `src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs:176`, `src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs:570`

**The Issue:** Channel-authenticated requests call `ResolveUserAsync` every turn, and profile application rewrites several fields to empty defaults when sparse channel tokens omit claims (roles/groups/custom claims/locale/timezone/org). This causes profile erosion plus write amplification.

**Why Static Analysis Missed It:** The code is type-correct and intentionally “refreshes”; analyzers do not reason about claim sparsity differences across auth contexts and long-term data drift.

**Impact:** Persisted identity context becomes unstable or degraded over time, and high-volume channels incur unnecessary DB writes/contention on every inbound turn.

**Recommended Fix:** Apply partial updates only for claims actually present, keep prior values for missing claims, and add change detection so `SaveChangesAsync` runs only when effective profile data changed.

**File/Module:** `src/Services/LeanKernel.Gateway/Sessions/DbAgentStateStore.cs:102`, `src/Services/LeanKernel.Gateway/Sessions/DbAgentStateStore.cs:119`

**The Issue:** `SaveSessionAsync` handles concurrency by reloading and overwriting (last-writer-wins), and broadly catches `DbUpdateException` as if it were duplicate insert. Non-duplicate failures can be misclassified, and one branch can exit without persisting or surfacing a clear failure signal.

**Why Static Analysis Missed It:** Presence of try/catch appears defensive; analyzers cannot assess semantic correctness of conflict resolution policy or exception taxonomy in a stateful retry path.

**Impact:** Under concurrent turns or transient persistence faults, agent state can be silently clobbered or dropped, causing non-deterministic conversation continuity bugs.

**Recommended Fix:** Handle concurrency with explicit conflict outcomes (retry with merge policy or return handled conflict to caller), catch provider-specific duplicate-key exceptions only, and rethrow/log all other `DbUpdateException` types.

### Top 3 Risks

1. Missing runtime approval/audit enforcement for side-effecting actions (trust-boundary failure).
2. Retry/idempotency contract mismatch across terminals and gateway persistence (duplicate/lost turns).
3. Identity profile drift from sparse channel claims plus per-turn writes (context corruption + DB pressure).
