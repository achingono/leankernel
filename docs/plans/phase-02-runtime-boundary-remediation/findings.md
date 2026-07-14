# Phase 02 Review Findings

Contextual / architectural review performed 2026-07-13 against the current worktree
(`src/Services/LeanKernel.Gateway`, `src/Common/LeanKernel.Logic`, `src/Common/LeanKernel.Data`,
`src/Common/LeanKernel.Core`). This pass deliberately excludes anything SonarQube already covers
(style, coverage, CVEs, generic hotspots) and focuses on business logic, trust boundaries,
state lifecycle, and resource economics that a static analyzer cannot reason about.

Each finding maps to the remediation scope in `index.md`. Severity: **Critical** (unsafe under
real multi-tenant/concurrent load), **Major** (correctness/economics defect), **Suggestion**
(maintainability / hardening).

---

## Critical

### C1 — Forwarded-host trust allows cross-tenant impersonation
- **File/Module:** `Services/LeanKernel.Gateway/Programs.cs:36-44,172` → `Requests/HostNameAccessor.cs:24` → `Logic/Providers/IdentityResolver.cs:17-26`
- **The Issue:** `ForwardedHeadersOptions` enables `XForwardedHost` and then *clears* `KnownProxies` and `KnownIPNetworks`. With both allow-lists empty, the middleware accepts `X-Forwarded-Host` from **any** client. `HostNameAccessor` reads `Request.Host.Host` and `IdentityResolver.ResolveTenantAsync` maps that host directly to a `TenantEntity`. Any unauthenticated caller can send `X-Forwarded-Host: victim-tenant.example` and have the entire request resolved into another tenant's partition.
- **Why Static Analysis Missed It:** The code compiles and each line is individually correct. Sonar does not model the runtime coupling between "forwarded header trust is unrestricted" and "host is the tenant authorization key." It sees a config block and a DB lookup, not a trust-boundary bypass.
- **Impact:** Full tenant isolation break. An attacker reads/writes sessions, transcripts, agent state, and memory belonging to any tenant simply by spoofing a header. Under load this is a silent cross-tenant data-leak vector, not a crash.
- **Recommended Fix:** Restrict forwarded-header processing to the real deployment proxy set (`KnownProxies` / `KnownIPNetworks` populated from config), or disable `XForwardedHost` and resolve the tenant from an authenticated/verified host source. Fail closed when the host is not on a configured allow-list.
  ```csharp
  options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
  foreach (var net in trustedProxies.Networks) options.KnownIPNetworks.Add(net);
  // Only enable XForwardedHost when a trusted proxy set is configured.
  ```

### C2 — Tenant resolution fails open to `Guid.Empty`
- **File/Module:** `Services/LeanKernel.Gateway/Providers/RequestContextPermit.cs:119-120` (and the `Guid.Empty` fallbacks at 47/57/67/102-111)
- **The Issue:** `_resolvedTenantId = tenant?.Id ?? Guid.Empty;`. When the host does not resolve to an active tenant, the request continues with `TenantId == Guid.Empty` instead of being rejected. All downstream ownership rows (`SessionEntity`, `TurnEntity`, `AgentStateEntity`, memory scope) are then written and later matched under the shared `Guid.Empty` partition.
- **Why Static Analysis Missed It:** `?? Guid.Empty` is idiomatic null-coalescing; Sonar has no notion that `Guid.Empty` is a *shared security principal* here rather than a harmless default.
- **Impact:** Every request whose host is unknown/inactive is silently merged into a single global partition. Different unauthenticated visitors (and misconfigured hosts) read each other's sessions and memory. This is data commingling that only appears under real multi-host traffic.
- **Recommended Fix:** Treat an unresolved/inactive tenant as a hard failure (`401/404`) before any ownership row is created. Do not manufacture a `Guid.Empty` tenant/user/channel identity for real HTTP requests; reserve the empty-context branch strictly for non-HTTP background work.

### C3 — Memory search and memory save use different scopes → cross-tenant recall
- **File/Module:** `Services/LeanKernel.Gateway/Providers/GBrainMemoryClient.cs:40-45,86-89` ↔ `Logic/Providers/MemoryProvider.cs:34-39,85-90`
- **The Issue:** `SaveMemoryAsync` scopes writes by a path slug: `memory/{TenantId}/{UserId}/{ChannelId}/{key}`. `SearchMemoriesAsync` scopes reads by `namespace_name = scope.Namespace` — but `MemoryProvider` builds `MemoryScope` **without setting `Namespace`** (it only sets Tenant/User/Channel). So every search runs with `namespace_name = null` while writes are path-partitioned. The two halves of the isolation contract do not agree: reads are effectively unscoped (or scoped to a null/global namespace) even though writes are tenant-pathed.
- **Why Static Analysis Missed It:** Both calls are type-correct and pass a `MemoryScope`. Sonar cannot see that the *save* uses `scope.TenantId/UserId/ChannelId` while the *search* uses `scope.Namespace` (never populated) — a semantic divergence across two files and an external MCP contract.
- **Impact:** Memory retrieval is not partitioned by tenant/user/channel. One tenant's fact-extraction results can surface in another tenant's prompt context (a privacy leak and a prompt-poisoning vector), and legitimately saved memories may never be found by search. Grows worse as the shared memory store fills.
- **Recommended Fix:** Derive **one** canonical scope token from Tenant/User/Channel and use it for both operations. Either populate `MemoryScope.Namespace` from `TenantId/UserId/ChannelId` and have `put_page` write into that same namespace, or have `search` filter by the identical slug prefix used by `BuildScopedSlug`. Add a regression test proving a memory saved under scope A is never returned when searching under scope B.

### C4 — JWT bearer validation fully disabled + `/v1/*` unauthenticated → identity spoofing
- **File/Module:** `Services/LeanKernel.Gateway/Programs.cs:74-84,184-185` → `Logic/Providers/IdentityResolver.cs:29-55`
- **The Issue:** The Bearer handler disables `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime`, **and `ValidateIssuerSigningKey`**. Any string that parses as a JWT is accepted as authenticated, with `IsAuthenticated == true`. `ResolveOrCreateUserAsync` then trusts the unverified `sub`/`NameIdentifier` claim to look up or create the persisted user. A caller can forge `{"sub":"<victim>"}` (no signature) and be resolved as that persisted user. Additionally, `MapOpenAIResponses()`/`MapOpenAIConversations()` carry no `RequireAuthorization()`, so the anonymous path is also open with no documented policy.
- **Why Static Analysis Missed It:** Disabling individual validation flags may raise a generic hotspot, but Sonar cannot connect "signature validation off" to "the `sub` claim is the persisted-identity primary key," which is what turns a weak-config smell into full account takeover across the identity partition.
- **Impact:** Authorization is effectively advisory. An attacker forges any user or tenant-member identity and operates inside that user's persisted sessions/memory. Combined with C1/C2 this collapses the entire identity-partitioning model.
- **Recommended Fix:** Decide and implement the intended protection model (per `index.md` open decision): require real signature/issuer/audience/lifetime validation for authenticated access, and make the anonymous branch an explicit, rate-limited, storage-controlled policy rather than an accidental default. Add coverage asserting a forged/unsigned token is rejected.

### C5 — Tool turns rehydrate as user messages (transcript trust-boundary corruption)
- **File/Module:** `Logic/Providers/DbChatHistoryProvider.cs:56-62` (write side: 165-168, 192-194)
- **The Issue:** Request-side `tool` messages are persisted with `Role = "tool"` (`message.Role.ToString().ToLowerInvariant()`), but on rehydration the role switch only handles `user`/`system`/`assistant` and falls through `_ => ChatRole.User`. Stored tool output is therefore replayed into the model as if the **user** authored it. The fail-open default silently rewrites message provenance.
- **Why Static Analysis Missed It:** The `switch` is exhaustive-looking and has a default arm, which Sonar treats as correct/defensive. It has no model of "tool output resurfaced as user input" being a semantic/security regression.
- **Impact:** On any conversation that used tools, the next turn's history presents tool results as user statements. This degrades model behavior and creates a prompt-injection channel: tool output (potentially attacker-influenced, e.g. web/file search) is elevated to trusted user intent. Manifests only after a tool turn is persisted and reloaded.
- **Recommended Fix:** Round-trip roles losslessly — add `"tool" => ChatRole.Tool` and drop the fail-open default (skip or explicitly log unknown roles). Persist and restore tool-call/tool-result content structurally rather than flattening to text where meaning depends on role. Add a tool-role round-trip regression test.

---

## Major

### M1 — Agent-state persistence downgrades concurrency conflicts to last-write-wins
- **File/Module:** `Services/LeanKernel.Gateway/Sessions/DbAgentStateStore.cs:71,95-110`; token defined at `Data/EntityContext.cs:103` (`RowVersion.IsRowVersion()`)
- **The Issue:** `AgentStateEntity.RowVersion` is a configured optimistic-concurrency token, but `SaveSessionAsync` defeats it: it calls `ChangeTracker.Clear()`, re-reads, and on `DbUpdateException` (which includes `DbUpdateConcurrencyException`) simply re-loads the row and **overwrites** `StateJson` with the in-flight value, then saves again. The concurrency check becomes a blind clobber.
- **Why Static Analysis Missed It:** There is a `try/catch` around `SaveChangesAsync`, so Sonar sees "concurrency handled." It cannot tell that the handler discards the conflicting winner's state instead of merging or retrying.
- **Impact:** Two concurrent turns on the same conversation (parallel requests, retries, multi-tab) silently lose one side's agent state. Corruption is invisible — no error surfaces. The `RowVersion` column exists but buys nothing.
- **Recommended Fix:** Load the row (tracked), apply changes, and let EF compare `RowVersion` on save. On `DbUpdateConcurrencyException`, reload-and-merge (or return a handled conflict result the caller retries) instead of overwriting. Remove the pre-emptive `ChangeTracker.Clear()` that discards the tracked original.

### M2 — Duplicate tenant relationship / shadow FK on `SessionEntity`
- **File/Module:** `Core/Entities/TenantEntity.cs:77` (`ICollection<SessionEntity> Sessions`) ↔ `Data/EntityContext.cs:75-78` (`HasOne(e => e.Tenant).WithMany()`); manifested in `Data/Migrations/20260713173605_InitialSchema.cs:114,132-133,201-203`
- **The Issue:** `SessionEntity.Tenant` is mapped to the explicit `TenantId` FK via `.WithMany()` (no inverse navigation). Because `TenantEntity.Sessions` exists but is left unmapped, EF infers a *second* tenant→session relationship and generates a shadow nullable FK `TenantEntityId` plus `IX_Sessions_TenantEntityId` and `FK_Sessions_Tenants_TenantEntityId`. The schema now has two tenant links for every session, only one of which is populated.
- **Why Static Analysis Missed It:** This is EF model-convention drift expressed in generated migration code. Sonar does not evaluate the EF metamodel, so the redundant relationship is invisible to it.
- **Impact:** Every `SessionEntity` carries an always-null `TenantEntityId` column and an unused index (write and storage overhead), and the model implies a tenant relationship that ownership queries never use — a latent correctness and maintenance trap. Loading `Tenant.Sessions` would query the wrong (empty) FK.
- **Recommended Fix:** Wire the inverse explicitly — `HasOne(e => e.Tenant).WithMany(t => t.Sessions).HasForeignKey(e => e.TenantId)` — so the single `TenantId` FK backs the navigation, then generate a follow-up migration dropping `TenantEntityId`, its index, and its FK. Validate no duplicate tenant FK remains in the snapshot.

### M3 — Unbounded chat-history retrieval; compaction fields declared but unused
- **File/Module:** `Logic/Providers/DbChatHistoryProvider.cs:48-64`; unused fields `Core/Entities/TurnEntity.cs:43,48` (`IsCompacted`, `CompactionSourceId`)
- **The Issue:** `ProvideChatHistoryAsync` loads **every** turn for a session (`Where(SessionId).ToListAsync()`), orders in memory, and returns all of them each turn. There is no cap, windowing, or summarization, and the `IsCompacted`/`CompactionSourceId` scaffolding is never read or written.
- **Why Static Analysis Missed It:** The query is correct and efficient per-call. Sonar cannot see that the result set grows without bound over a conversation's lifetime and is re-sent to the model on every turn.
- **Impact:** Long-lived conversations cause linear growth in DB reads, memory, prompt size, and token cost per turn — eventually exceeding the model context window and degrading/erroring. A slow-burn resource-economics failure, not a crash.
- **Recommended Fix:** Bound retrieval (recent-N turns and/or token budget, `OrderByDescending(Timestamp).Take(N)` at the DB), and add a compaction/summarization path that populates `IsCompacted`/`CompactionSourceId` on a defined trigger (turn count / token estimate / age). Keep a small verbatim recent window plus a tested summary of older turns.

### M4 — No idempotency/replay protection on transcript and memory writes
- **File/Module:** `Logic/Providers/DbChatHistoryProvider.cs:68-89,158-184`; `Logic/Providers/MemoryProvider.cs:83-152`
- **The Issue:** `StoreChatHistoryAsync` unconditionally inserts request/response turns, and `MemoryProvider.StoreCoreAsync` extracts and saves facts, with no operation key or dedup guard. A client retry, proxy re-send, or streaming reconnection re-runs the same logical request and duplicates turns and memory facts.
- **Why Static Analysis Missed It:** The inserts are valid; there is no data-flow signal that the same logical request may arrive twice. Idempotency is a distributed-systems property outside Sonar's scope.
- **Impact:** Under real network conditions, transcripts accumulate duplicate turns (further inflating M3) and memory fills with duplicate/near-duplicate facts that skew retrieval scoring. Duplication is permanent and compounds.
- **Recommended Fix:** Introduce a stable per-request operation key (e.g. from the OpenAI request/response id) recorded on `TurnEntity`/memory metadata, and make the write path upsert-by-key so retries are no-ops. Scope the key to one logical request so legitimate repeated content is not suppressed (see risk R4).

### M5 — Guest-user resolution ignores tenant scope
- **File/Module:** `Logic/Providers/IdentityResolver.cs:102-141` (param `tenantId` unused); global uniqueness at `Data/EntityContext.cs:132` (`(Issuer, Subject)` unique)
- **The Issue:** `ResolveGuestUserAsync(Guid tenantId, ...)` never uses `tenantId`. Guests are looked up/created purely by `Issuer == "anonymous" && Subject == sessionId`, and `UserEntity` enforces global `(Issuer, Subject)` uniqueness. Guest identity is therefore tenant-agnostic, contradicting ADR 0002's persisted, tenant-partitioned identity model.
- **Why Static Analysis Missed It:** An unused parameter may be a minor smell, but Sonar cannot know it encodes a required isolation dimension the ADR mandates. The lookup is otherwise valid.
- **Impact:** Anonymous identities are not partitioned by tenant. If a session id is ever reused across tenants (or the anonymity model changes), a guest row bleeds across tenant boundaries, and guest ownership carries no tenant linkage for auditing.
- **Recommended Fix:** Include `tenantId` in the guest lookup and creation, persist it on the guest user, and make anonymous uniqueness `(TenantId, Issuer, Subject)`. Align the key with C1/C2 so guest ownership is always tenant-anchored.

### M6 — Anonymous identity keyed on an ephemeral ASP.NET session id → instability + unbounded row growth
- **File/Module:** `Services/LeanKernel.Gateway/Providers/RequestContextPermit.cs:38-39,132-136`; `Programs.cs:69-71,178`
- **The Issue:** Anonymous isolation uses `HttpContext.Session.Id`. ASP.NET only persists the session (and its id) once something is written to it; nothing in this pipeline writes to `Session`. `Session.Id` thus returns a fresh, non-persisted GUID that changes across requests, and the fallback `SessionId ?? Guid.NewGuid()` guarantees a new id when session is absent.
- **Why Static Analysis Missed It:** `Session.Id` is a valid property access. The "id is not stable until the session is committed" behavior is an ASP.NET runtime contract Sonar does not model.
- **Impact:** Each anonymous request can resolve to a **new** guest user and a new isolation key — breaking conversation continuity for anonymous users and creating an unbounded stream of guest `UserEntity` rows and orphaned sessions/agent-state. A memory/row-growth leak under anonymous traffic plus broken UX.
- **Recommended Fix:** Force session materialization (write a marker so `Session.Id` persists via the session cookie) before using it as an identity key, or derive anonymous continuity from a dedicated signed cookie. Verify a repeat anonymous request maps to the same guest user.

### M7 — Synchronous blocking on async identity resolution in the request path
- **File/Module:** `Services/LeanKernel.Gateway/Providers/RequestContextPermit.cs:119-149` (`.GetAwaiter().GetResult()` ×4)
- **The Issue:** `EnsureResolved` runs four sequential async DB calls (`ResolveTenantAsync`, `ResolveOrCreateUserAsync`/`ResolveGuestUserAsync`, `ResolveOrCreateChannelAsync`) via `.GetAwaiter().GetResult()`, blocking the request thread. `IPermit` members are synchronous, so every consumer that first touches `TenantId`/`UserId` pays this cost on a pool thread.
- **Why Static Analysis Missed It:** Sync-over-async may raise a generic warning, but Sonar cannot weigh the architectural cost of blocking on multiple DB round-trips on the hot request path under concurrency.
- **Impact:** Thread-pool starvation and latency amplification under load — each concurrent request parks a thread across several DB calls. Also serializes lazily on first property access, making the cost easy to miss in profiling.
- **Recommended Fix:** Make identity resolution async end-to-end (e.g. an `IPermitAccessor.ResolveAsync()` resolved once in middleware and cached in `HttpContext.Items`), so consumers read already-resolved values. If `IPermit` must stay synchronous, resolve eagerly in async middleware before handlers run.

---

## Suggestions

### S1 — Service-locator anti-pattern in permit resolution
- **File/Module:** `Services/LeanKernel.Gateway/Providers/RequestContextPermit.cs:114`
- **The Issue:** `serviceProvider.GetRequiredService<IIdentityResolver>()` pulls a dependency out of the container mid-method instead of constructor injection, hiding the dependency and complicating testing/lifetime reasoning.
- **Why Static Analysis Missed It:** It is a valid DI call; the pattern concern is architectural, not a rule violation.
- **Impact:** Harder to test and reason about scope; masks a real dependency of the permit.
- **Recommended Fix:** Inject `IIdentityResolver` via the primary constructor (it is already scoped). The `_resolving` re-entrancy guard suggests this was worked around; explicit injection removes the need.

### S2 — Silent broad catch resets state without diagnostics
- **File/Module:** `Sessions/DbAgentStateStore.cs:43-46`; `Logic/Providers/MemoryProvider.cs:65-69`
- **The Issue:** Deserialization failures in `GetSessionAsync` and search failures in `ProvideAIContextAsync` are swallowed with empty `catch` blocks that fall back to "new/empty," discarding state with no log. This conflicts with the `AGENTS.md` rule against broad exception swallowing.
- **Why Static Analysis Missed It:** Empty catch may be flagged generically, but the contextual harm — a corrupt state blob silently resetting a conversation — is invisible to Sonar.
- **Impact:** A single bad state row silently wipes a user's durable session; memory outages are indistinguishable from "no memories." Debugging is blind.
- **Recommended Fix:** Log the exception with correlation (conversation id / scope) before degrading, and consider distinguishing transient (retry) from permanent (reset) failures.

### S3 — `IMemoryClient` scope contract is easy to under-fill
- **File/Module:** `Logic/Providers/IMemoryClient.cs:6-27`; misuse at `Logic/Providers/MemoryProvider.cs:34-39,85-90`
- **The Issue:** `MemoryScope` exposes `Namespace` as an optional field that the search path depends on but callers routinely omit (root cause of C3). The contract lets Logic silently drop the scope dimension the transport needs.
- **Why Static Analysis Missed It:** Optional properties are legal; Sonar cannot know one is effectively required for correct isolation.
- **Impact:** Recurring isolation bugs whenever a new caller forgets to set `Namespace`.
- **Recommended Fix:** Make the scope a single required, transport-computed value (e.g. build the canonical namespace inside the client from Tenant/User/Channel) so Logic cannot omit it. See open decision in `risk-register.md`.

---

## Traceability

| Finding | Scope item in `index.md` | Exit-criteria checkbox |
| --- | --- | --- |
| C1 | Forwarded-host trust rework | Forwarded-header trust |
| C2 | Fail-closed tenant resolution | Unresolved-tenant rejection |
| C3 | Memory-scope enforcement | Memory search/save same scope |
| C4 | `/v1/*` protection model | `/v1/*` access model + coverage |
| C5 | Tool-message semantics | Tool-role round-trip |
| M1 | Optimistic concurrency | Agent-state conflict handling |
| M2 | EF model cleanup | Single tenant relationship |
| M3 | Bounded history + compaction | Bounded retrieval / compaction |
| M4 | Replay/idempotency | Replay-safe writes |
| M5 | Tenant-scoped guest identity | Guest resolution tenant-scoped |
| M6 | Tenant-scoped anonymous identity | (extends guest/anon scope) |
| M7 | Boundary hardening (perf) | (supporting, no dedicated gate) |
| S1–S3 | Maintainability hardening | (supporting) |
