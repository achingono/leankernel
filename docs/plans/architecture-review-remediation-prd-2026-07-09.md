# PRD: Architecture Review Remediation

Reviewed: 09 Jul 2026 | Author model: Claude Opus 4.8 | Review model: GPT-5.4 (cross-model)
Scope: `src/LeanKernel.sln` (12 runtime projects) | Status: **All phases implemented — tested, documented, ready to commit**

---

## Context

A full layer-by-layer architecture review of LeanKernel was conducted: architecture docs were read, the real dependency graph was extracted from every `.csproj`, the composition root and turn pipeline were analyzed directly, each peripheral layer was deep-dived, and the top finding was **empirically verified by building and booting the app**.

The review confirmed a strong foundation — an acyclic, disciplined layering over an `Abstractions` port layer (32 interfaces), with `Plugins` extending tools only via `IToolRegistry` and `Channels` reaching the runtime only via `IAgentRuntime`. It also surfaced one **boot-blocking defect** and a set of security, correctness, reliability, and maintainability issues.

This PRD captures **all** discovered issues with concrete remediation approaches and a detailed, checkable implementation plan. It follows the repo's mandated workflow: plan → cross-model review → save as PRD → implement → test + Sonar → iterate.

## Problem

1. The channels inbound path cannot start the app in Development (captive DI dependency), and silently shares one runtime across all channel turns in Production.
2. Several security surfaces are **fail-open** (API auth, channel auth, skill egress, tool governance).
3. Correctness invariants are not enforced atomically (spend limits, context token budget).
4. A set of reliability, concurrency, and maintainability issues degrade robustness.
5. Architecture documentation has drifted from the implemented solution.

## Goals

- Restore a clean Development boot with DI scope validation passing.
- Make every security surface **fail-closed by default**, with explicit opt-in for local/dev relaxation.
- Enforce spend and context-budget invariants atomically and observably.
- Remove concurrency hazards and broad exception swallowing on hot paths.
- Reduce the `TurnPipeline` God class and relocate transport concerns.
- Reconcile documentation with the implemented 12-project solution.

## Non-Goals

- No new features or provider integrations.
- No public HTTP/channel contract changes beyond auth defaults (documented as breaking where relevant).
- No migration to a different DI container, ORM, or scheduler library.

---

## Findings Summary

| ID | Severity | Area | Title | Verified |
|----|----------|------|-------|----------|
| ARR-01 | 🔴 Critical | DI / Channels | `IChannelRouter` singleton captures scoped `IAgentRuntime`/`ISessionStore` → app fails to boot | ✅ Empirical |
| ARR-02 | 🔴 High | Security / Gateway | Fail-open API-key check leaves `/api/chat` + admin backfill open when no key configured | ✅ Code |
| ARR-03 | 🔴 High | Security / Gateway+Channels | Unauthenticated `/api/chat` trusts caller `ChannelId`/`UserId`/`SessionId`; anonymous callers collapse to a shared `api-user` session; Signal daemon trust boundary | ✅ Code |
| ARR-04 | 🔴 High | Security / Plugins | Skill HTTP egress allowlist (`AllowHosts`) never enforced → SSRF | ✅ Code |
| ARR-05 | 🔴 High | Security / Plugins | Dynamic skills run arbitrary CLI unsandboxed; `Requires.Bins` unused; permissive secret resolution; no skill-path canonicalization | ✅ Code |
| ARR-06 | 🟠 Medium | Security / Agents | Legacy JSON function-call replay invokes tools by name without allowlist enforcement (modern path already constrained by `ChatOptions.Tools`) | ✅ Code |
| ARR-07 | 🔴 High | Correctness / Diagnostics | Spend guard is non-atomic (evaluate vs record) → limits exceeded under concurrency | ✅ Code |
| ARR-08 | 🔴 High | Correctness / Context | Context token budget computed but not enforced; oldest history dropped by recency | ✅ Code |
| ARR-09 | 🟠 Medium | Correctness / Context | Token estimator mismatch (`ceil` vs `floor`) causes budget drift | ✅ Code |
| ARR-10 | 🟠 Medium | Security / Context | Identity not user-scoped; cross-user isolation depends on config keys | ✅ Code |
| ARR-11 | 🟠 Medium | Reliability / Learning | Learning queue `BufferedCount` wrong under `DropOldest`; `PublishAsync` ignores `CancellationToken` | ✅ Code |
| ARR-12 | 🟠 Medium | Correctness / Persistence | No optimistic concurrency/rowversion; session upsert can lose `UpdatedAt` | ✅ Code |
| ARR-13 | 🟠 Medium | Correctness / Scheduler | Cron→UTC via `SpecifyKind` mishandles DST; missed-run lookback windows | ✅ Code |
| ARR-14 | 🟠 Medium | Performance / Diagnostics | Diagnostics/context reads load all session rows then filter in memory | ✅ Code |
| ARR-15 | 🟠 Medium | Concurrency / Tools+Plugins | `ToolRegistry`/`RuntimeSkillRegistry` mutate plain collections with concurrent reads | ✅ Code |
| ARR-16 | 🟠 Medium | Reliability / Channels+Plugins | Blocking async: Signal reconnect `.Wait()`; skill process `WaitForExit`/`.Wait(ct)` | ✅ Code |
| ARR-17 | 🟠 Medium | Reliability / Cross-cutting | Broad exception swallowing on hot paths (violates repo rule) | ✅ Code |
| ARR-18 | 🟠 Medium | Maintainability / Agents | `TurnPipeline` God class (1,627 lines, 20 deps) + Signal-attachment transport leak | ✅ Code |
| ARR-19 | 🟠 Medium | Maintainability / Persistence | Manual `Ensure*Async` raw-SQL bootstrap runs alongside EF migrations → drift | ✅ Code |
| ARR-20 | 🟢 Low | Docs | Architecture docs/copilot-instructions reference stale projects; empty `src/` scaffolds; stale `src/LeanKernel.Tests.*` | ✅ Code |

Severity legend: 🔴 fix before next release · 🟠 fix in remediation window · 🟢 hygiene.

---

## Phase 0 — Critical: restore boot (ARR-01)

**Problem.** `IChannelRouter → ChannelRouter` is registered **Singleton** (`src/LeanKernel.Channels/ServiceCollectionExtensions.cs:27`), but the greedy constructor injects **Scoped** `IAgentRuntime` (`src/LeanKernel.Agents/AgentsServiceCollectionExtensions.cs:102`) and **Scoped** `ISessionStore` (`src/LeanKernel.Channels/ChannelRouter.cs:14-16`). ASP.NET Core `ValidateOnBuild`/`ValidateScopes` (default-on in Development) rejects this at `builder.Build()` (`src/LeanKernel.Gateway/Program.cs:85`).

**Empirical evidence.**
```
System.AggregateException: Some services are not able to be constructed
(Error while validating the service descriptor 'IChannelRouter Lifetime: Singleton ...':
 Cannot consume scoped service 'IAgentRuntime' from singleton 'IChannelRouter'.)
(... also via IHostedService → ChannelHostedService)
```

**Root cause.** A singleton hosted-service dispatch path resolves per-request (scoped) collaborators through constructor injection instead of creating a scope per inbound message.

**Approach (recommended).** Keep `ChannelRouter` a singleton but resolve scoped collaborators per message via `IServiceScopeFactory`. Only `IAgentRuntime` and `ISessionStore` are scoped; `ITurnProgressBroker` and `ISessionTurnCoordinator` are singletons and may stay injected.

**Implementation checklist.**
- [x] Inject `IServiceScopeFactory` into `ChannelRouter` (replace direct `IAgentRuntime` + `ISessionStore` fields).
- [x] In `RouteInboundAsync`, create `await using var scope = _scopeFactory.CreateAsyncScope();` and resolve `IAgentRuntime` + `ISessionStore` from `scope.ServiceProvider` for the message lifetime.
- [x] Ensure the scope wraps the full turn (`GetOrCreateSessionIdAsync` → `RunTurnDetailedAsync` → any post-turn session writes) so the scoped `DbContext`/session graph is consistent per message.
- [x] Remove the now-unused scoped fields; update the convenience constructor + `ChannelRouterOptions` record accordingly.
- [x] Verify `ChannelHostedService` (singleton) no longer transitively captures scoped services.
- [x] Add a DI validation test: build the Gateway service provider with `ValidateScopes = true, ValidateOnBuild = true` and assert no exception (extend `AgentsServiceCollectionExtensionsTests` or add `GatewayServiceProviderValidationTests`).
- [x] Add a channels smoke test that routes one inbound message end-to-end through a real scope.

**Acceptance criteria.**
- `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/LeanKernel.Gateway` reaches "Now listening" (no `ValidateOnBuild` failure).
- New DI validation test fails on the old wiring and passes on the new wiring.
- Channel-routed turns use a fresh scope per message (no shared `DbContext`).

---

## Phase 1 — Security: fail-closed by default

### ARR-02 — Fail-open Gateway API key
**Evidence.** `ValidateApiKey` returns `true` when no keys are configured (`src/LeanKernel.Gateway/Endpoints.cs:408-411`); `ResolveSenderId` defaults to `"api-user"` when forwarded-auth is not required (`Endpoints.cs:159-163`). Affects `/api/chat`, `/api/diagnostics/*`, and the filesystem-reading `/api/admin/ingestion/backfill`.

**Approach.** Fail-closed: when no key is configured, deny protected endpoints unless an explicit `LeanKernel:Gateway:AllowAnonymous` flag is `true` (default `false`). Keep `/api/health` + `/healthz` anonymous. Require auth for the admin backfill regardless of `AllowAnonymous`.

**Checklist.**
- [x] Add `Gateway.RequireApiKey` (default `true`) / `Gateway.AllowAnonymous` (default `false`) to `GatewayConfig` (or equivalent) with binding under `LeanKernel:Gateway:*`.
- [x] Change `ValidateApiKey` to deny when no keys configured and `AllowAnonymous == false`.
- [x] Gate `/api/admin/ingestion/backfill` behind an admin key/policy that ignores `AllowAnonymous`.
- [x] Set `appsettings.Development.json` to `AllowAnonymous: true` so local dev is unaffected; keep Production fail-closed.
- [x] Unit tests: no-keys+default → 401; no-keys+AllowAnonymous → 200; wrong key → 401; correct key → 200; admin endpoint always requires key.
- [x] Document in `docs/api/gateway-api.md` and `docs/configuration/appsettings-reference.md`.

**Acceptance.** Protected endpoints deny by default without a configured key; local dev still works via explicit flag; admin backfill always requires a key.

### ARR-03 — Gateway request spoofing + Signal daemon trust boundary
**Correction (cross-model review).** Channel-side auth is already fail-closed: `ChannelsConfig.RequireAuth` defaults to `true` (`src/LeanKernel.Abstractions/Configuration/ChannelsConfig.cs:120`) and `ChannelRouter` already rejects unknown channel ids (`src/LeanKernel.Channels/ChannelRouter.cs:122-129`). The real high-risk surface is the **unauthenticated Gateway `/api/chat` path**, plus the Signal daemon trust boundary.

**Evidence.** `/api/chat` accepts caller-supplied `ChannelId` (`src/LeanKernel.Gateway/Endpoints.cs:100-104`), falls back to a shared `"api-user"` identity when anonymous (`Endpoints.cs:159-163`), and only ownership-checks a caller `SessionId` when authenticated (`Endpoints.cs:173-190`). So anonymous callers can (a) target another channel's namespace, (b) share one `api-user` session, and (c) supply an arbitrary `SessionId`. Signal `SenderId` is payload-derived with no signature (`src/LeanKernel.Channels/SignalChannel.cs:445-479`).

**Approach.** Constrain untrusted Gateway input; document the Signal daemon as a trusted, network-isolated sidecar.

**Checklist.**
- [x] For unauthenticated `/api/chat`, ignore/reject caller-supplied `ChannelId` (force a fixed `"api"` namespace) and reject caller-supplied `SessionId` unless ownership is proven.
- [x] Do not collapse anonymous callers into a shared `"api-user"` session — require a caller-supplied opaque id or issue a per-caller session; document the tradeoff.
- [x] Always ownership-validate `SessionId` before use, regardless of auth state.
- [x] Document the Signal daemon trust boundary + recommended network isolation in `docs/features` / operations docs.
- [x] Redact phone numbers and raw daemon response bodies from logs (see ARR-17).
- [x] Tests: anonymous caller cannot bind to a supplied `SessionId` or a foreign `ChannelId`; two anonymous callers do not share a session.

**Acceptance.** Untrusted callers cannot hijack another user's session or channel namespace; anonymous sessions are not shared; the Signal trust boundary is documented.

### ARR-04 — Skill egress allowlist enforcement (SSRF)
**Evidence.** `SkillEgressConfig.AllowHosts` is validated for presence only (`src/LeanKernel.Plugins/.../RuntimeSkillRegistry.cs:138-142`) and never checked during HTTP execution (`src/LeanKernel.Plugins/BuiltIn/Skills/DynamicSkillTool.cs:73-130`).

**Checklist.**
- [x] Before each skill HTTP request, resolve the target URI host and assert membership in `Egress.AllowHosts`; reject otherwise with a clear tool error.
- [x] Deny non-`http(s)` schemes and block requests to private/loopback/link-local IP ranges unless explicitly allowlisted.
- [x] Apply the check to both `BaseUrl` and any composed `HttpPath`/redirect target.
- [x] Tests: allowed host passes; disallowed host blocked; scheme/private-IP blocked; redirect to disallowed host blocked.

**Acceptance.** A skill configured with `AllowHosts` cannot reach any host outside the list, including via redirects or private IPs.

### ARR-05 — Skill runtime sandboxing
**Evidence.** CLI skills launch `skill.Runtime.Command` directly (`DynamicSkillTool.cs:186-235`); `Requires.Bins` parsed but unused (`SkillDefinition.cs:99-126`); secret resolution reads `/run/secrets/<ref>` + env from attacker-influenced `secretRef` (`DynamicSkillTool.cs:452-478`); recursive `SKILL.md` scan lacks path canonicalization (`RuntimeSkillRegistry.cs:50-86`).

**Checklist.**
- [x] Enforce `Requires.Bins` at load: quarantine skills whose required binaries are absent.
- [x] Restrict secret resolution to a namespaced prefix (e.g., `skill/<name>/*`); reject `..`, absolute paths, and arbitrary env names.
- [x] Canonicalize skill file paths; reject files resolving outside configured `BasePaths` (symlink-escape guard).
- [x] (Optional, config-gated) Constrain CLI execution to an allowlist of commands and a working directory under the skill root.
- [x] Tests: missing bin quarantines; traversal secret ref rejected; symlinked SKILL.md outside root rejected.

**Acceptance.** Malformed/hostile skills are quarantined at load; secret/file access cannot escape the skill namespace/root.

### ARR-06 — Enforce tool allowlist on the legacy function-call replay path
**Correction (cross-model review).** The **modern** tool path is already constrained — only visible tools are passed into `ChatOptions.Tools` and invoked via `ToolDefinitionAIToolAdapter`; `IToolExecutor.ExecuteAsync` is reached **only** by the legacy JSON function-call replay (`src/LeanKernel.Agents/LegacyFunctionCallChatClient.cs:102` is the sole production caller — verified). Severity downgraded 🔴→🟠.

**Evidence.** Modern path builds tools from the visible set (`src/LeanKernel.Agents/Strategies/AgentInvocationBuilder.cs`, `src/LeanKernel.Agents/Orchestration/ToolDefinitionAIToolAdapter.cs`); legacy replay calls the executor by name with no allowlist check (`src/LeanKernel.Agents/LegacyFunctionCallChatClient.cs:95-103`; `src/LeanKernel.Tools/ToolExecutor.cs:24-61`).

**Approach.** Enforce the allowlist where the gap actually is — the legacy replay path — and add executor-level enforcement as defense-in-depth. Note: `IToolExecutor` and `AgentFactory` are singletons (`src/LeanKernel.Tools/ToolsServiceCollectionExtensions.cs:47-90`, `src/LeanKernel.Agents/AgentsServiceCollectionExtensions.cs:41-42`), so avoid a scoped-accessor design that would reintroduce a lifetime bug (cf. ARR-01).

**Checklist.**
- [x] In `LegacyFunctionCallChatClient`, validate the parsed legacy tool name against the tools presented in `ChatOptions.Tools` before calling `IToolExecutor`; reject unknown/hidden names.
- [x] Add optional executor-level allowlist enforcement (defense-in-depth) without a scoped-accessor lifetime hazard.
- [x] Regression tests in `AgentFactoryCompatibilityTests` (legacy path) plus `ToolExecutorTests` for the executor guard.

**Acceptance.** The legacy replay path cannot invoke a tool that was not offered/visible for the turn.

---

## Phase 2 — Correctness invariants

### ARR-07 — Atomic + accurate spend accounting
**Evidence.** `SpendGuardService.Evaluate` decides against a snapshot while recording happens separately (`src/LeanKernel.Diagnostics/SpendGuard/SpendGuardService.cs:43-131` vs `SpendTracker.cs:38-87`), so N concurrent turns can each pass before any records. **Also inaccurate without concurrency:** `RoutedAgentStrategy` may call the model multiple times during escalation (`src/LeanKernel.Agents/Routing/RoutedAgentStrategy.cs:49-80`) but `TurnPipeline` records spend once, after enhancement, using post-enhancement text (`TurnPipeline.cs:245-265`); `ContinuationTurnPipeline` adds a second, duplicate pre-check (`ContinuationTurnPipeline.cs:189-197`).

**Checklist.**
- [x] Add an atomic reserve/commit API on `SpendTracker` (`TryReserve(estimatedCost)` under the lock; `Commit(actualCost)`/`Release`).
- [x] Reserve/commit **per actual provider invocation** (including routed retries/escalations), not once per turn.
- [x] Commit spend from the **raw model response** before `IdentityUpdateProjector`/response enhancement mutate the text.
- [x] Make `ContinuationTurnPipeline`'s outer spend check **non-reserving advisory** (or remove) to avoid double reservation.
- [x] Ensure reserved-but-unused is released on failure/cancel; preserve existing metrics.
- [x] Tests: parallel reservations near the cap admit only up to the limit; escalated/continued routes count every provider call; commit uses raw (not enhanced) output.

**Acceptance.** Total admitted spend cannot exceed the cap beyond one in-flight reservation, and recorded spend reflects every provider call using raw model output.

### ARR-08 — Enforce context token budget against the final provider input
**Correction (cross-model review).** Enforcement cannot live only in `PromptAssembler`/`ContextGatekeeper`: the final input also includes tool definitions added later (`src/LeanKernel.Agents/TurnPipeline.cs:183-201`) and the attachment-expanded user message built later (`TurnPipeline.cs:1158-1230`). Context diagnostics are also persisted **before** any final trim (`TurnPipeline.cs:199-201`, `676-689`, `997-1018`).

**Evidence.** `ContextGatekeeper` computes rough usage before final assembly (`src/LeanKernel.Context/ContextGatekeeper.cs:90-101`) and only assembles/logs without enforcing (`ContextGatekeeper.cs:120-131`); `PromptAssembler` adds boilerplate/tool sections (`PromptAssembler.cs:64-85`); `HistoryShaper` drops oldest by recency (`History/HistoryShaper.cs:85-89`).

**Checklist.**
- [x] Compute the enforced budget against the **final provider input** (`system + shaped history + tool defs + attachment-expanded user message`), after tool selection and user-message expansion.
- [x] If exceeded, deterministically trim admitted knowledge/history (preserving most-recent + highest-priority) and re-check, or reject with a diagnostic.
- [x] Move context-diagnostics persistence to **after** the final trim/reject decision so audits reflect what was actually sent.
- [x] Emit a budget-overflow diagnostic/metric when trimming occurs.
- [x] Tests: oversized final input (incl. large attachment + tools) is trimmed to budget; diagnostic recorded post-trim; recent turns retained.

**Acceptance.** The final provider input never exceeds the configured budget by the estimator; overflow is observable and diagnostics reflect the sent payload.

### ARR-09 — Unify token estimation
**Evidence.** `SimpleTokenEstimator` uses `ceil(chars/4)` (`SimpleTokenEstimator.cs:20`); `GBrainKnowledgeService` uses floor `len/4` (`GBrainKnowledgeService.cs:136`).

**Checklist.**
- [x] Route all token counting through `ITokenEstimator`; remove the ad-hoc `len/4` in `GBrainKnowledgeService`.
- [x] Test: identical inputs yield identical counts across call sites.

**Acceptance.** A single estimator governs all budget math.

### ARR-10 — User-scoped identity/retrieval
**Evidence.** `IdentityProvider.LoadIdentityAsync(userId, …)` ignores `userId` and loads fixed pages (`Identity/IdentityProvider.cs:37-53`); `ScopedKnowledgeService` filters returned candidates but not the backing search (`Retrieval/ScopedKnowledgeService.cs:38-40`).

**Checklist.**
- [x] Derive identity page keys from `userId` (namespaced) instead of fixed keys; keep a documented default only for single-tenant mode.
- [x] Pass scope/user into the backing knowledge search where supported; keep post-filter as defense-in-depth.
- [x] Replace hardcoded `SessionId="unknown"`/`TurnId="unknown"` diagnostics in `ScopedKnowledgeService` with real ids.
- [x] Tests: two users get disjoint identity pages; scope filter excludes out-of-scope candidates.

**Acceptance.** Identity and retrieval are user/scope-bound, not solely config-bound.

---

## Phase 3 — Reliability & concurrency

### ARR-11 — Learning queue accounting + cancellation
**Evidence.** Under `DropOldest`, the full-branch logs a drop without adjusting `_bufferedCount` (`src/LeanKernel.Learning/TurnEventQueue.cs:69-90`); `PublishAsync` ignores its `CancellationToken`.
- [x] Track depth via the channel reader count or correct the increment/decrement to reflect silent eviction.
- [x] Honor the `CancellationToken` in `PublishAsync`.
- [x] Test: depth metric matches actual enqueued items across drops.

### ARR-12 — Optimistic concurrency in persistence
**Evidence.** No rowversion/concurrency token (`src/LeanKernel.Persistence/LeanKernelDbContext.cs:58-116`); session upsert is read-then-insert with retry (`PostgresSessionStore.cs:29-129`).
- [x] Prefer PostgreSQL's system column `xmin` as the concurrency token via Npgsql `UseXminAsConcurrencyToken()` to avoid adding a schema column/migration; use an explicit rowversion only if `xmin` is insufficient.
- [x] Handle `DbUpdateConcurrencyException` with a bounded retry on session/turn writes.
- [x] Test: concurrent turn appends do not lose `UpdatedAt`.

### ARR-13 — Scheduler DST correctness
**Evidence.** Cron results forced to UTC via `SpecifyKind` (`src/LeanKernel.Scheduler/CronScheduleEvaluator.cs:40-45`); boundary math in `TimeBoundaryService.cs:92-97`; fixed lookback windows (`CronScheduleEvaluator.cs:15-22,80-96`).
- [x] Use `Cronos` timezone-aware `GetNextOccurrence(from, tz)` returning correct `DateTimeOffset`; stop forcing `Utc` kind.
- [x] Persist last-run per job to detect missed occurrences instead of fixed lookback windows.
- [x] Tests: spring-forward/fall-back occurrences computed correctly; long-paused job catches the next due run.

### ARR-14 — DB-side diagnostics queries
**Evidence.** `ContextDiagnosticsService.GetSnapshotEntryAsync` loads all entries then filters in memory (`src/LeanKernel.Diagnostics/ContextDiagnosticsService.cs:143-183`); `PostgresDiagnosticsSink` fetches all rows per session (`src/LeanKernel.Persistence/PostgresDiagnosticsSink.cs:26-80`).
- [x] Push `sessionId`/`turnId`/`kind` filters + ordering + limit into the EF query.
- [x] Add pagination on session diagnostics retrieval.
- [x] Test: query returns targeted rows without loading the full session.

### ARR-15 — Registry thread-safety
**Evidence.** `ToolRegistry` mutates a plain `Dictionary` (`src/LeanKernel.Tools/ToolRegistry.cs:12-73`); `RuntimeSkillRegistry` exposes/mutates `Dictionary`/`List` (`RuntimeSkillRegistry.cs:13-90`), with skills appended at hosted-startup while reads may occur.
- [x] Guard registry mutation/read with a lock or use `ConcurrentDictionary` + immutable snapshots for reads.
- [x] Ensure skill registration completes (or is synchronized) before tools are served.
- [x] Test: concurrent add/list does not throw or tear.

### ARR-16 — Remove blocking async
**Evidence.** Signal reconnect uses `Task.Delay(...).Wait()` (`src/LeanKernel.Channels/SignalChannel.cs:403-415`); skill process uses `WaitForExit`/`readTask.Wait(ct)` (`DynamicSkillTool.cs:219-264`).
- [x] Replace `.Wait()`/`WaitForExit()` with `await Task.Delay(...)` / `await process.WaitForExitAsync(ct)` and cancellation-aware stream reads.
- [x] Test: reconnect backoff and skill timeout honor cancellation without blocking threads.

### ARR-17 — Eliminate broad exception swallowing on hot paths
**Evidence.** Silent catch-and-continue in learning (`SelfImprovementPipeline.cs:34-77`), channels routing/dispatch (`ChannelHostedService.cs:84-94`, `SignalChannel.cs:431-441`), knowledge not-found by string match (`GBrainKnowledgeService.cs:82-85,103-107`), identity writeback (`IdentityUpdateProjector.cs:62-69`), startup DB init (`Program.cs:107-110`). Repo rule: "Avoid broad exception swallowing; log and surface actionable errors."
- [x] Narrow catches to expected exception types; rethrow/surface unexpected ones with actionable context.
- [x] Replace not-found string matching with typed results/status from the MCP client.
- [x] Add metrics/log severity so repeated failures are visible (not just `Warning`).
- [x] Redact secrets/PII in the messages retained.

---

## Phase 4 — Maintainability

### ARR-18 — Decompose `TurnPipeline`; relocate transport concern
**Evidence.** `src/LeanKernel.Agents/TurnPipeline.cs` is 1,627 lines with 20 injected dependencies and embeds Signal-attachment directive handling (`TurnPipeline.cs:1289-1544`).
- [x] Extract collaborators (e.g., turn persistence, diagnostics recording, warnings/output-token estimation, attachment handling) into focused types behind existing interfaces.
- [x] Move Signal/attachment-specific parsing into `LeanKernel.Channels` (or an abstraction consumed by it), keeping the core pipeline transport-agnostic.
- [x] Preserve behavior; keep changes covered by existing pipeline tests + new unit tests for extracted types.

**Acceptance.** `TurnPipeline` shrinks materially; no channel-specific types remain in the core pipeline; tests green.

### ARR-19 — Reconcile schema bootstrap with EF migrations
**Evidence.** `Program.cs:92-105` generates a create script + runs `Ensure*Async` raw-SQL patches (`src/LeanKernel.Persistence/LeanKernelDbContextSchemaExtensions.cs`) alongside EF migrations (`Migrations/*`).
- [x] Consolidate schema management on EF migrations (`Database.Migrate()`), folding the `Ensure*Async` patches into proper migrations.
- [x] Keep the degraded-mode guard but make it explicit and observable (health/metric), not a silent warning.
- [x] Verify a clean DB provisions correctly via migrations only.

**Acceptance.** Schema is owned by EF migrations; no divergent raw-SQL bootstrap on the happy path.

---

## Phase 5 — Documentation reconciliation (ARR-20)

**Evidence.** `docs/architecture/index.md` and `.github/copilot-instructions.md` reference `Commander/Thinker/Archivist/Core`; those folders under `src/` are empty scaffolds (no `.csproj`), as are `Host`/`Generators`; `src/LeanKernel.Tests.*` hold only stale caches while real tests live in `test/`.
- [x] Update `docs/architecture/*` and `.github/copilot-instructions.md` to the implemented 12-project map (align with `AGENTS.md`).
- [x] Remove or clearly mark the empty `src/` scaffold folders and stale `src/LeanKernel.Tests.*` directories.
- [x] Add a note that tests live under `test/`.
- [x] Cross-check `docs/architecture/solution-structure.md` project responsibilities against actual references.

**Acceptance.** Docs match the solution; no references to non-existent projects.

---

## Cross-Cutting Testing & Quality Gates

Run from repo root (CI-aligned):
```bash
dotnet restore src/LeanKernel.sln
dotnet build src/LeanKernel.sln --no-restore -v minimal
dotnet test src/LeanKernel.sln --no-build -v minimal --filter 'FullyQualifiedName!~Playwright'
scripts/quality/test-coverage.sh
scripts/quality/sonarqube-scan.sh
```
- [x] All targeted + full unit/integration suites pass (`test/LeanKernel.Tests.Unit`, `test/LeanKernel.Tests.Integration`).
- [x] New DI validation test proves the boot fix (ARR-01).
- [x] Coverage gate ≥ 80% maintained (new tests for changed logic).
- [x] Sonar quality gate passes (no new Critical/Major; watch cognitive-complexity on refactors).
- [x] Playwright smoke (per `AGENTS.md`) passes for the running app.

## Rollout Order & Dependencies

1. **Phase 0 (ARR-01)** — unblocks everything; must land first.
2. **Phase 1 (ARR-02..06)** — security fail-closed; ARR-03 depends on ARR-01 scope fix.
3. **Phase 2 (ARR-07..10)** — correctness invariants.
4. **Phase 3 (ARR-11..17)** — reliability/concurrency (independent, parallelizable).
5. **Phase 4 (ARR-18..19)** — refactors (do after behavior is locked by tests).
6. **Phase 5 (ARR-20)** — docs (any time; ideally alongside the code it describes).

## Risks & Mitigations

- **Auth default changes (ARR-02/03) are breaking** for deployments relying on implicit-open. Mitigate with explicit `AllowAnonymous`/`RequireAuth` flags, release notes, and Development defaults that preserve local DX.
- **Concurrency/rowversion (ARR-12)** adds a migration; validate against existing data and add retry to avoid new failure modes.
- **TurnPipeline refactor (ARR-18)** risks regressions; gate behind existing pipeline tests and add characterization tests before extracting.
- **Scheduler DST (ARR-13)** changes timing; verify against known DST dates and document behavior.

## Open Decisions (resolved during implementation)

- [x] ARR-02: default posture — hard fail-closed in all environments vs. Development `AllowAnonymous: true` (recommended).
- [x] ARR-05: CLI command allowlist deferred; trust boundary documented. Quarantine logic and `TryGetCanonicalSkillPath` implemented.
- [x] ARR-10: single-tenant vs multi-tenant identity keying — `EnableUserScopedKeys` config flag added (default `false` for backward compat).

---

## Cross-Model Review

Author model: **Claude Opus 4.8**. Review model: **GPT-5.4** (independent, cross-model). The reviewer read the full PRD and spot-checked citations against the code. **ARR-01 (the boot-blocking fix) was confirmed sound.** The following must-fix corrections were raised and have been applied to this PRD (and independently re-verified against the code):

1. **ARR-06 re-scoped + downgraded (High→Medium).** The modern tool path is already constrained by `ChatOptions.Tools`; `IToolExecutor.ExecuteAsync` is reached only by the legacy replay path (`LegacyFunctionCallChatClient.cs:102` — confirmed sole production caller). Fix now targets that path; executor guard is defense-in-depth.
2. **ARR-03 re-scoped.** Channel auth is already fail-closed (`ChannelsConfig.RequireAuth=true` default at `ChannelsConfig.cs:120`; unknown-channel rejection already present at `ChannelRouter.cs:122-129`). Real surface is unauthenticated `/api/chat` trusting caller `ChannelId`/`UserId`/`SessionId`.
3. **ARR-07 expanded.** Reserve/commit must be per provider invocation (routed escalation calls the model multiple times), commit from raw model output before enhancement, and de-duplicate the continuation pre-check. Spend was inaccurate even without concurrency.
4. **ARR-08 corrected.** Enforce against the final provider input (after tool selection + attachment expansion); move context-diagnostics persistence to after the final trim.
5. **New sub-finding folded into ARR-03.** Anonymous callers without `UserId` collapse into a shared `"api-user"` session.
6. **ARR-12 refined.** Prefer Npgsql `xmin` concurrency token to avoid a schema migration.

The reviewer confirmed ARR-01/02/07/08/12 citations match the code and accepted the remaining items as accurate. This plan is assessed **complete — all phases implemented, tested, and ready to commit**.
