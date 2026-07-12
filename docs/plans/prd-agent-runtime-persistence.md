# PRD — Identity‑Partitioned Agent Runtime on Microsoft Agent Framework

| | |
|---|---|
| **Status** | Draft — ready for implementation |
| **Owner** | @achingono |
| **Target framework** | .NET 10 (`net10.0`) |
| **Primary dependency** | [Microsoft Agent Framework](https://github.com/microsoft/agent-framework) `Microsoft.Agents.AI` **1.13.0** (+ Hosting `1.13.0-preview.260703.1`, Hosting.OpenAI `1.13.0-alpha.260703.1`, DevUI `1.13.0-preview.260703.1`) |
| **Related** | Rebuild of the original `leankernel` project (`~/source/repos/leankernel`) |

---

## 1. Summary

LeanKernel is being rebuilt as a thin, framework‑native agent runtime that **leverages as much of the Microsoft Agent Framework (MAF) as possible** while preserving the original project's vision: a deterministic, inspectable turn pipeline with durable, identity‑scoped chat history and GBrain‑backed contextual memory, exposed over standard OpenAI‑compatible HTTP endpoints.

Today the solution **does not build**. The `LeanKernel.Logic` project contains a half‑written `DbChatHistoryProvider` (invalid `switch` syntax), an empty `IMemoryClient`, a non‑functional `SessionState`, and DI wiring that fights the framework (name collisions, `BuildServiceProvider()` at registration time, unresolvable services, scoped‑vs‑named agent mismatch). This PRD defines the scope, target architecture, and a phased implementation checklist to:

1. **Resolve all build errors** by correctly implementing the five missing core capabilities on top of MAF:
   - an **ASP.NET‑hosted `AIAgent`** (including structured output),
   - an **`AgentSession`** bound to the ASP.NET session and the authenticated user identity,
   - a **`ChatHistoryProvider`** backed by EF Core and filtered by user identity,
   - an **`AIContextProvider`** backed by **GBrain** and filtered by user identity,
   - **ASP.NET‑hosted OpenAI Responses endpoints**.
2. **Partition and filter** all session and context data using persisted `Tenant`, `User`, and `Channel` identities resolved from the request host and authenticated principal.
3. Ship **unit, integration, and Playwright API‑endpoint tests**.

---

## 2. Goals & Non‑Goals

### 2.1 Goals

- **G1 — Green build.** `dotnet build` succeeds for every project with `TreatWarningsAsErrors=true` (see `src/Directory.Build.props`).
- **G2 — Framework‑native.** Prefer MAF primitives (`AIAgent`, `ChatClientAgent`, `AgentSession`, `AgentSessionStore`, `SessionIsolationKeyProvider`, `ChatHistoryProvider`, `AIContextProvider`, `AddAIAgent`, `AddOpenAIResponses`/`MapOpenAIResponses`) over bespoke abstractions. Custom code exists **only** to durably persist and to scope by identity.
- **G3 — Identity partitioning.** Every session/turn/memory read or write is scoped to `(TenantId, UserId, ChannelId)` for authenticated users, and `(TenantId, UserId, SessionId, ChannelId)` for anonymous users where `UserId` resolves to a persisted guest user. A user can never read or mutate another user's data across any tenant or channel.
- **G4 — Durable persistence.** Chat history and agent‑session state survive process restarts, stored via EF Core (SQL Server / SQLite / PostgreSQL, selected by connection string — matching current `Programs.cs`).
- **G5 — GBrain memory.** Contextual memory retrieval reuses the original project's GBrain MCP stack, admitted into the prompt via an `AIContextProvider`, scoped by identity.
- **G6 — OpenAI‑compatible surface.** The agent is reachable via `/v1/responses` and `/v1/conversations`, plus DevUI in Development.
- **G7 — Test coverage.** Unit (xUnit + FluentAssertions + Moq + EFCore.InMemory), integration (`WebApplicationFactory<Program>`), and Playwright API tests validating the hosted endpoints and partitioning guarantees.

### 2.2 Non‑Goals

- Blazor operator UI / Portal / Client terminals (README aspirational projects) — **out of scope** for this PRD.
- Tools/plugins (`SKILL.md`), scheduler, learning pipeline, channels — **out of scope** here (future PRDs).
- Re‑implementing GBrain itself or the MCP transport — we **reuse** the original's client verbatim.
- Migrating historical data from the original PostgreSQL `engine` schema.

---

## 3. Vision Alignment (original LeanKernel)

The original project (`~/source/repos/leankernel`) established these principles that this rebuild must honor:

- **Deterministic turn pipeline** — persist user turn → gate context → assemble prompt → invoke → (optional) enhance → persist assistant turn + emit event (`docs/features/turn-pipeline.md`). In the rebuild, MAF's provider pipeline (`ChatHistoryProvider` + `AIContextProvider` around `ChatClientAgent`) fulfills this role; our custom providers add persistence and gating.
- **Partition by identity** — original sessions are keyed by `(ChannelId, UserId)` (`PostgresSessionStore.cs`: `Where(s => s.ChannelId == channelId && s.UserId == userId)`). The rebuild extends this to `(TenantId, UserId, ChannelId)` by resolving the request host to `TenantEntity`, the authenticated principal to `UserEntity`, and the HTTP surface to `ChannelEntity`.
- **Retrieval supplies candidates; gating decides admission** — GBrain returns candidates; a scope/namespace policy decides what enters the prompt (`ScopedKnowledgeService`). The rebuild's `MemoryProvider` (an `AIContextProvider`) performs this admission.
- **GBrain as the memory/knowledge substrate** — reuse `GBrainMcpClient`, `GBrainKnowledgeService`, `ScopedKnowledgeService`, and the `RetrievalCandidate`/`KnowledgePage` DTOs.

---

## 4. Current State Analysis

### 4.1 Solution layout (actual)

Four projects (no `.sln` yet):

| Project | Kind | References | Builds? |
|---|---|---|---|
| `src/Common/LeanKernel.Core` | classlib | — | ✅ |
| `src/Common/LeanKernel.Data` | classlib | Core, EF Core 10.0.9 | ✅ |
| `src/Common/LeanKernel.Logic` | classlib | Core, Data, `Microsoft.Agents.AI*` 1.13.0 | ❌ |
| `src/Services/LeanKernel.Gateway` | web | Core, Data, Logic, Hosting.OpenAI, DevUI, EF providers | ❌ (depends on Logic) |

> The README lists many additional projects (Knowledge, Tools, Plugins, Channels, etc.) and `src/LeanKernel.sln`; these **do not exist** and are aspirational. This PRD only touches the four real projects and adds test projects.

### 4.2 Enumerated build errors & defects

**Blocking compile errors (from `dotnet build`):**

- **B1** — `src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs:38` — invalid expression `Role = switch (m.Role) { ... }`. Must be `Role = m.Role switch { ... }`. Causes ~30 cascading parser errors (`CS1525`, `CS1513`, `CS1026`, `CS8803`, …).
- **B2** — `DbChatHistoryProvider.cs:36` references `m.AuthorName`, but `TurnEntity` has no `AuthorName` property.
- **B3** — `MemoryProvider` calls `_memoryClient.LoadMemoriesAsync(...)`, but `IMemoryClient` (`Providers/IMemoryClient.cs`) is an **empty** interface.
- **B4** — `src/Services/LeanKernel.Gateway/Programs.cs:2` imports `using LeanKernel.Requests;`, but the actual namespace is `LeanKernel.Gateway.Requests`.

**Design/runtime defects that will fail correctness once B1-B4 are fixed:**

- **D1 — Providers take `AgentSession` via constructor.** `DbChatHistoryProvider` and `MemoryProvider` accept `AgentSession session` and build `new SessionState(session)`. MAF supplies the session through the **invocation context** (`InvokingContext.Session` / `InvokedContext.Session`), *not* the constructor. `AgentSession` is abstract and cannot be DI‑constructed. Remove session from constructors; read it from context.
- **D2 — `SessionState` is inert.** `SessionState.SessionId` is get‑only and never assigned; the constructor ignores its `session` argument, so `SessionId` is always `null`. History queries would silently match nothing.
- **D3 — `async` without `await`.** `DbChatHistoryProvider.ProvideChatHistoryAsync` is `async` but never awaits (CS1998 → error under `TreatWarningsAsErrors`).
- **D4 — DI name collision / recursion.** `LeanKernel.Logic` defines `AddChatClient(this IServiceCollection)` that internally calls the built‑in `Microsoft.Extensions.AI` `services.AddChatClient(chatClient)` — same simple name, high risk of self‑recursion/ambiguity. Rename to `AddLeanKernelChatClient`.
- **D5 — `BuildServiceProvider()` at registration time.** Both `Logic` (`AddChatClient`) and `Gateway` (`AddAgent`) call `services.BuildServiceProvider()` inside a registration extension, creating a throwaway container (duplicate singletons, options not yet bound). Replace with factory lambdas that resolve from the real provider.
- **D5b — same antipattern in Data registration.** `LeanKernel.Data` `AddEntityContext` also builds a throwaway container to resolve interceptors; these interceptor instances are detached from request scope and can not reliably resolve request services.
- **D6 — Unresolvable agent dependencies.** `Gateway/AddAgent` does `provider.GetRequiredService<ChatHistoryProvider>()` and `GetRequiredService<IEnumerable<AIContextProvider>>()`, but `Logic` registers the concrete `DbChatHistoryProvider`/`MemoryProvider` types, not their base types. Register against the base types (`ChatHistoryProvider`, `AIContextProvider`).
- **D7 — Agent lifetime/name mismatch.** `Gateway/AddAgent` registers an **unnamed, scoped** `AIAgent`, but `app.MapOpenAIResponses()` (no‑arg) dispatches to **named** agents resolved via `AddAIAgent(name, factory)`. Register the agent by name.
- **D8 — Permit wiring is incomplete.** `IPermit` and `ClaimsPermit<>` already exist, but `AddPermits()` is never called from `Programs.cs`, and the current claims-only permit shape is insufficient for tenant/user/channel-aware partitioning.
- **D8b — interceptor permit resolution path is unsafe.** Interceptors currently use `eventData.Context.GetService<IPermit>()`; this resolves from EF internal services, not app DI by default. Interceptors must be DI-resolved from the real container and use constructor injection (or an equivalent supported pattern).
- **D9 — `EntityContext` has no model config.** No `OnModelCreating`, no indexes, no partition columns, no session↔turn relationship configuration, no soft‑delete query filter.
- **D10 — Identity abstractions live in the wrong assembly.** `IPrincipalAccessor`/`IHostNameAccessor` are declared in the `LeanKernel.Gateway` project (namespace `LeanKernel.Gateway.Requests`). `LeanKernel.Logic` (providers) and `LeanKernel.Data` (interceptors/permit) should not depend on Gateway types.
- **D11 — No configuration.** No `appsettings.json` / `appsettings.Development.json`; `OpenAISettings`, `AgentSettings`, `GBrainConfig`, and `ConnectionStrings` are never bound. `AddOpenAIResponses`/conversations use in‑memory stores (non‑durable, non‑partitioned) by default.
- **D12 — `Program` not test‑visible.** `Programs.cs` uses top‑level statements; `WebApplicationFactory<Program>` requires `public partial class Program;`.
- **D13 — `EntityContext` constructor mismatch.** `EntityContext` lacks a `DbContextOptions<EntityContext>` constructor, which blocks proper options/interceptor configuration and provider-specific behavior.
- **D14 — auth/authorization middleware wiring mismatch.** `app.UseAuthentication()` and `app.UseAuthorization()` are called, but `AddAuthentication(...)`/`AddAuthorization()` are not called in startup. This causes runtime pipeline failures and prevents principal-based partitioning from working as designed.
- **D15 — duplicate provider registration.** `DbChatHistoryProvider` is registered twice (`AddContextProviders` and `AddChatHistoryProviders`), creating ambiguous lifetime/duplication behavior.
- **D16 — Hosting primitives location/package mismatch.** `DbAgentSessionStore` and `SessionIsolationKeyProvider` need `Microsoft.Agents.AI.Hosting`; if implemented in `LeanKernel.Logic`, add package reference there, otherwise keep these implementations in `LeanKernel.Gateway`.
- **D17 — anonymous principal fallback is unsafe.** `TryAddScoped<IPrincipal>(... Principal!)` assumes a non-null principal and does not provide a deterministic anonymous identity/session partition.
- **D18 — ASP.NET session middleware is missing.** The design requires anonymous fallback to ASP.NET session id, but startup does not currently register/use session services.
- **D19 — forwarded headers are configured but never applied.** `ForwardedHeadersOptions` is registered, but `app.UseForwardedHeaders()` is never called. Behind a reverse proxy, tenant resolution would use the proxy host instead of the original tenant host.
- **D20 — authenticated identity is resolved from claims instead of persisted users.** `ClaimsPrincipalExtensions.Id()` only accepts GUID-valued `sid`/`NameIdentifier`/`sub`; common OIDC `sub` values are opaque strings, which currently collapse to `Guid.Empty`. Authenticated partitioning should resolve `UserEntity.Id`, not derive identity directly from claims.
- **D21 — channel resolution is undefined for the HTTP/OpenAI surface.** `SessionEntity.ChannelId` is required, but the PRD does not define how `/v1/responses` and `/v1/conversations` resolve or seed the canonical `ChannelEntity`.
- **D22 — conversation ID, scoped conversation ID, and persisted chat-session ID are conflated.** The current plan reads a `conversationId` from `AgentSession.StateBag` and uses it to query `TurnEntity.SessionId`, but `TurnEntity.SessionId` references `SessionEntity.Id`, not the OpenAI conversation id. The model needs explicit mapping between external conversation ids, internal scoped ids, and persisted chat-session ids.
- **D23 — ASP.NET session prerequisites are incomplete.** Anonymous fallback requires not just `AddSession`/`UseSession`, but also a backing session cache (`AddDistributedMemoryCache` or another `IDistributedCache` implementation).
- **D24 — `IPermit` shape no longer matches the partition model.** The current contract exposes only `Id`, `Badge`, and `HostName`, but the new canonical partition requires `TenantId`, non-null `UserId`, `ChannelId`, and anonymous session identity.
- **D25 — tenant/user/channel resolution layer is missing.** The runtime needs a request-scoped resolver that maps host -> `TenantEntity`, principal -> `UserEntity`, and route/surface -> `ChannelEntity` before any provider, interceptor, or session-store logic runs.

---

## 5. Target Architecture

### 5.1 Component map

```
                         ┌──────────────────────── HTTP (OpenAI-compatible) ────────────────────────┐
                         │  POST /v1/responses   POST /v1/conversations   GET /devui (dev only)      │
                         └───────────────────────────────┬──────────────────────────────────────────┘
                                                           │  MapOpenAIResponses()
                                                          ▼
   Request.Host / Principal ─┐                ┌──────────────────────────┐
   Tenant/User/Channel       ├──► Request     │  Named AIAgent           │  AddAIAgent("leankernel", factory)
   Resolution Layer          │    Permit      │  = ChatClientAgent       │
   IPermit / IRequestContext ┘                │   • IChatClient (OpenAI) │
                                  │           │   • ChatHistoryProvider ─┼──► DbChatHistoryProvider (EF Core, tenant/user/channel-filtered)
                                  │           │   • AIContextProviders ──┼──► MemoryProvider (GBrain, tenant/user/channel-filtered)
                                  ▼           └───────────┬──────────────┘
                    IsolationKeyScopedAgentSessionStore   │  AgentSession (per conversation)
                                  │  wraps                ▼
                          DbAgentSessionStore ────────► EntityContext (TenantEntity, UserEntity, ChannelEntity, SessionEntity, TurnEntity, AgentSessionEntity)
                               (EF Core)                        │
                                                                ▼
                                                        SQL Server / SQLite / PostgreSQL
```

### 5.2 Identity & partitioning (Requirement #2)

**Canonical partitioning model** (D20-D25): use persisted domain identities, not raw hostnames or claim-derived GUIDs.

- Resolve `TenantEntity` from the normalized request host.
- Resolve `UserEntity` from authenticated identity (`issuer` + `subject`) and use `UserEntity.Id` as the canonical user key.
- Resolve unauthenticated requests to a persisted tenant-scoped guest `UserEntity` so runtime-owned rows never persist a null `UserId`.
- Resolve `ChannelEntity` from the HTTP/OpenAI surface and use `ChannelEntity.Id` as the canonical channel key.
- Build a request-scoped permit/context object that exposes `TenantId`, non-null `UserId`, `ChannelId`, `HostName`, and authentication/session metadata.
- Keep `IPrincipalAccessor`/`IHostNameAccessor` implementations in Gateway as request inputs only; Logic/Data should consume the resolved permit/context, not raw host/principal accessors.

Suggested contract:

```csharp
public interface IPermit
{
    Guid TenantId { get; }
    Guid UserId { get; }
    Guid ChannelId { get; }
    string HostName { get; }
    bool IsAuthenticated { get; }
    string? SessionId { get; }
    Badge Badge { get; }
}
```

**Interceptor resolution fix** (D5b, D8b): register interceptors through the real DI container and use constructor-injected dependencies. Do not resolve `IPermit` via `DbContext.GetService<T>()` from EF internal services.

**Implement `SessionIsolationKeyProvider`** — `IdentityIsolationKeyProvider : SessionIsolationKeyProvider`:

```csharp
public sealed class IdentityIsolationKeyProvider(
    IPermit permit,
    IHttpContextAccessor httpContextAccessor) : SessionIsolationKeyProvider
{
    public override ValueTask<string> GetSessionIsolationKeyAsync(CancellationToken ct = default)
    {
        var subjectKey = permit.IsAuthenticated
            ? permit.UserId.ToString()
            : permit.SessionId ?? throw new InvalidOperationException("Session is required for anonymous isolation.");

        return ValueTask.FromResult($"{permit.TenantId}|{permit.ChannelId}|{subjectKey}");
    }
}
```

This is the framework's supported extension point for per-user/per-tenant/per-channel partitioning. Anonymous users still resolve to a persisted guest `UserEntity.Id`, but fall back to the ASP.NET session id as an additional isolation dimension within the resolved tenant/channel boundary; authenticated users use the resolved persisted `UserEntity.Id`.

Implementation detail: provide a deterministic anonymous principal fallback (no null-forgiving `Principal!`), enable ASP.NET session (`AddDistributedMemoryCache`, `AddSession`, `UseSession`), and apply `UseForwardedHeaders()` before anything that reads `Request.Host` so tenant resolution and anonymous isolation keys are stable per browser session and original host.

### 5.3 AgentSession bound to ASP.NET session + identity (Requirement #1b)

- **`DbAgentSessionStore : AgentSessionStore`** — durably persists serialized `AgentSession` state. Place in `LeanKernel.Logic` only if that project references `Microsoft.Agents.AI.Hosting`; otherwise keep in `LeanKernel.Gateway` and preserve clean dependency direction. Overrides:
  - `GetSessionAsync(AIAgent agent, string conversationId, CancellationToken)` → load blob, `agent.DeserializeSessionAsync(json, …)`.
  - `SaveSessionAsync(AIAgent agent, string conversationId, AgentSession session, CancellationToken)` → `agent.SerializeSessionAsync(session, …)`, upsert blob.
  - Backed by a new `AgentSessionEntity { ScopedConversationId (PK), TenantId, UserId, ChannelId, StateJson, CreatedOn, UpdatedOn, RowVersion }`.
- **Wrap with `IsolationKeyScopedAgentSessionStore`** so every conversation id is transparently prefixed/escaped with the isolation key (`IsolationKeyScopedAgentSessionStore.GetScopedConversationIdAsync`). Register with `IsolationKeyScopedAgentSessionStoreOptions { Strict = true }` so cross‑isolation access is rejected, not silently created.
- **Keep ID domains explicit:**
  - `conversationId` = the externally visible OpenAI conversation id.
  - `scopedConversationId` = the internal isolation-prefixed id used by `IsolationKeyScopedAgentSessionStore` and conversation storage/index implementations.
  - `chatSessionId` = the persisted `SessionEntity.Id` used by `TurnEntity.SessionId`.
- Persist `conversationId` and `chatSessionId` as well-known state-bag keys on the `AgentSession`; never assume they are the same value.
- Registration:

  ```csharp
  services.AddScoped<SessionIsolationKeyProvider, IdentityIsolationKeyProvider>();
  services.AddScoped<DbAgentSessionStore>();
  services.AddScoped<AgentSessionStore>(sp => new IsolationKeyScopedAgentSessionStore(
      sp.GetRequiredService<DbAgentSessionStore>(),
      sp.GetRequiredService<SessionIsolationKeyProvider>(),
      new IsolationKeyScopedAgentSessionStoreOptions { Strict = true }));
  ```

> Net effect: internal storage keys are scoped to `(TenantId, UserId, ChannelId)` for authenticated users and `(TenantId, UserId, SessionId, ChannelId)` for anonymous users, while externally returned conversation ids remain stable and unscoped.

### 5.4 ChatHistoryProvider backed by EF Core, filtered by identity (Requirement #1c)

**`DbChatHistoryProvider : ChatHistoryProvider`** (`LeanKernel.Logic`). Corrected design:

- **No `AgentSession` in the constructor.** Inject `IDbContextFactory<EntityContext>` + `IServiceScopeFactory` (or only `IServiceScopeFactory`) and read the session from the context.
- **Scope/materialization rule.** Any scope/context created in provider methods must be disposed in-method *after* `ToListAsync`/materialization; never return lazy queryables across scope boundaries.
- `protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(InvokingContext context, CancellationToken ct)`:
  - Resolve `chatSessionId` from `context.Session.StateBag` (not the OpenAI `conversationId`) and the current `IPermit` (`TenantId`, `UserId`, `ChannelId`).
  - Query `EntityContext.Turns` joined to `Sessions` where `Session.TenantId == permit.TenantId && Session.ChannelId == permit.ChannelId && Session.UserId == permit.UserId && Session.Id == chatSessionId`, ordered by `Timestamp`.
  - Map each `TurnEntity` → `ChatMessage` with `Role = m.Role switch { "user" => ChatRole.User, "system" => ChatRole.System, "assistant" => ChatRole.Assistant, _ => ChatRole.User }`, `AuthorName = m.AuthorName`, `CreatedAt = m.Timestamp`, `Contents = [ new TextContent(m.Content) ]`. **Materialize** (`ToListAsync`) — do not return a lazy `IQueryable`.
- `protected override async ValueTask StoreChatHistoryAsync(InvokedContext context, CancellationToken ct)`:
  - Ensure the `SessionEntity` exists (create on first turn with `TenantId`, `UserId`, `ChannelId`, and external `ConversationId`).
  - On first create, persist the resulting `SessionEntity.Id` into `context.Session.StateBag["chatSessionId"]` so subsequent reads query the correct turn partition.
  - Persist `context.RequestMessages` (user/tool) and `context.ResponseMessages` (assistant) as `TurnEntity` rows stamped with `SessionId`, `Role`, `AuthorName`, `Content`, `Timestamp`. Honor the base‑class message filters (`StoreInputRequestMessageFilter`, etc.).
  - Save via `EntityContext.SaveChangesAsync` (audit/recyclable interceptors + `IPermit` apply automatically).

**Add `TurnEntity.AuthorName` (string?, nullable)** to fix B2, plus denormalized partition columns if we choose turn‑level filtering (see §5.8).

### 5.5 AIContextProvider backed by GBrain, filtered by identity (Requirement #1d)

**`MemoryProvider : AIContextProvider`** (`LeanKernel.Logic`). Corrected design:

- No `AgentSession` in the constructor. Depend on `IMemoryClient` (see below) and `IServiceScopeFactory`.
- `protected override async ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken ct)`:
  - Build the query text from `context.AIContext.Messages` (the request messages to be sent).
  - Resolve `(TenantId, UserId, ChannelId, conversationId)` from `IPermit` + `context.Session`, and pass the external `conversationId` into GBrain session-scoping APIs rather than the persisted `chatSessionId`.
  - Call `IMemoryClient.SearchMemoriesAsync(scope, query, maxResults, ct)` where `scope` encodes the identity/namespace (§5.7).
  - Enforce a token budget while admitting memories (use `RetrievalCandidate.TokenCount` where available) and include only the highest-value candidates.
  - Return `new AIContext { Messages = [ new ChatMessage(ChatRole.User, "<retrieved memories>") ] }` (or set `AIContext.Instructions`). Only admitted candidates are included (scope/namespace policy).
- `StoreAIContextAsync(InvokedContext context, …)` — optionally write salient facts back to GBrain (identity writeback), scoped to the user's namespace. May be a no‑op in phase 1.

**`IMemoryClient`** (fix B3) — define the real contract, delegating to the reused GBrain `IKnowledgeService`:

```csharp
public interface IMemoryClient
{
    Task<IReadOnlyList<MemoryItem>> SearchMemoriesAsync(MemoryScope scope, string query, int maxResults = 10, CancellationToken ct = default);
    Task SaveMemoryAsync(MemoryScope scope, string key, string content, CancellationToken ct = default);
}
```

`GBrainMemoryClient : IMemoryClient` wraps the original `IScopedKnowledgeService.RetrieveWithScopeAsync(...)` / `IKnowledgeService`, mapping `RetrievalCandidate` → `MemoryItem { Key, Text, Score, Source }`.

### 5.6 ASP.NET‑hosted AIAgent, incl. structured output (Requirement #1a)

Register a **named, singleton** agent via MAF hosting:

```csharp
services.AddAIAgent("leankernel", (sp, name) =>
{
    var settings = sp.GetRequiredService<IOptions<AgentSettings>>().Value;
    return new ChatClientAgent(
        sp.GetRequiredService<IChatClient>(),
        new ChatClientAgentOptions
        {
            Name = name,
            Description = settings.DefaultDescription,
            Instructions = settings.DefaultInstructions,          // ChatClientAgentOptions.ChatOptions for model params
            ChatHistoryProvider = sp.GetRequiredService<ChatHistoryProvider>(),
            AIContextProviders = sp.GetServices<AIContextProvider>().ToList(),
        },
        sp.GetRequiredService<ILoggerFactory>(),
        sp);
}, ServiceLifetime.Singleton);
```

- Registered against base types `ChatHistoryProvider`/`AIContextProvider` (fixes D6).
- No `BuildServiceProvider()`; the factory receives the real `sp` (fixes D5).
- **Structured output** is delivered by MAF's `AIAgent.RunAsync<T>(…)` generic overloads (the `AIAgentStructuredOutput` extensions). No custom work is required beyond exposing it; add a smoke test invoking `RunAsync<T>` with a simple record to prove structured output flows end‑to‑end.

### 5.7 ASP.NET‑hosted OpenAI Responses endpoints (Requirement #1e)

- **Services:** `builder.Services.AddOpenAIResponses();` and `AddOpenAIConversations();` (already present in `Programs.cs`).
- **Endpoints:** `app.MapOpenAIResponses();` (default path `/v1/responses`, agent resolved from DI) and `app.MapOpenAIConversations();`.
- **Durable, partitioned conversation storage (required):** defaults (`InMemoryConversationStorage`, `InMemoryAgentConversationIndex`) are non-durable/process-local. Implement EF-backed, identity-scoped `IConversationStorage` and `IAgentConversationIndex` (namespace `Microsoft.Agents.AI.Hosting.OpenAI.Conversations`) keyed internally by `scopedConversationId` from `IsolationKeyScopedAgentSessionStore`, with ownership recorded as `TenantId`, `UserId`, and `ChannelId`.
- **Do not leak internal IDs:** conversation APIs must continue to accept/return the unscoped external `conversationId`. Storage/index implementations must translate between external ids and scoped ids rather than returning isolation-prefixed identifiers to clients.
- **DevUI:** keep `if (app.Environment.IsDevelopment()) app.MapDevUI();` (note: gate on `app.Environment`, not `builder.Environment`).

### 5.8 Data model & persistence

`src/Common/LeanKernel.Core/Entities`:

- **`SessionEntity`** (exists) — persist `TenantId`, non-null `UserId`, `ChannelId`, and `ConversationId` (external OpenAI conversation id). Hostname is used to resolve `TenantId`, not as the primary persisted partition key. Anonymous requests must resolve to a persisted guest `UserEntity` so `UserId` remains non-null. Define a well-known channel constant for this HTTP surface (for example `"openai-http"`) or seed a corresponding `ChannelEntity`. Add unique indexes on `(TenantId, UserId, ChannelId, ConversationId)` plus lookup indexes covering anonymous session fallback.
- **`TurnEntity`** (exists) — add `AuthorName` (string?, B2). Filtering is via the parent `SessionEntity` (already carries `TenantId`/`UserId`/`ChannelId`); denormalize only if query plans require it.
- **`AgentSessionEntity`** (new) — `{ ScopedConversationId (PK, string), TenantId, UserId, ChannelId, StateJson (text), CreatedOn, UpdatedOn, RowVersion }` for `DbAgentSessionStore` (§5.3).

> Decision: keep `SessionEntity`/`TurnEntity` string IDs for MVP. Do **not** add `IAuditable`/`IRecyclable` to these entities in this PRD because those interfaces currently require `IEntity.Guid Id`.

`src/Common/LeanKernel.Data/EntityContext.cs` (fix D9):

- Add constructor `EntityContext(DbContextOptions<EntityContext> options) : base(options)` (D13).
- Add `DbSet<AgentSessionEntity> AgentSessions`.
- `OnModelCreating`: configure `SessionEntity`↔`TurnEntity` (1-to-many, cascade), foreign keys to `TenantEntity`/`UserEntity`/`ChannelEntity` where applicable, `Badge` complex type, indexes on partition columns plus `ConversationId`, a concurrency token on `AgentSessionEntity.RowVersion`, global query filter for `IRecyclable` soft delete, and JSON columns (`Metadata`, `StateJson`).
- Provide an EF Core migration per provider (or a single provider‑agnostic migration set) — `dotnet ef migrations add InitialAgentRuntime`.

### 5.9 ChatClient (OpenAI‑compatible)

Rename `AddChatClient` → `AddLeanKernelChatClient` (fix D4) and drop `BuildServiceProvider()` (fix D5):

```csharp
public static IServiceCollection AddLeanKernelChatClient(this IServiceCollection services)
{
    services.AddChatClient(sp =>
    {
        var cfg = sp.GetRequiredService<IOptions<OpenAISettings>>().Value;
        var client = new OpenAIClient(new ApiKeyCredential(cfg.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(cfg.BaseUrl) });
        return client.GetChatClient(cfg.DefaultModel).AsIChatClient();
    })
    .UseFunctionInvocation()
    .UseLogging();
    return services;
}
```

Bind `OpenAISettings` from `configuration.GetSection("OpenAI")`. This supports OpenAI, Azure OpenAI, or any OpenAI‑compatible gateway (LiteLLM, as in the original) via `BaseUrl`.

### 5.10 GBrain knowledge stack (reused from original)

Align to the decided split: **memory abstractions in `LeanKernel.Logic`, GBrain implementation in `LeanKernel.Gateway`**.

- `LeanKernel.Logic` owns `IMemoryClient`, `MemoryItem`, `MemoryScope`, and provider-facing contracts.
- `LeanKernel.Gateway` hosts GBrain adapters and transport wiring.

Reuse from `~/source/repos/leankernel/src/LeanKernel.Knowledge`:

- `GBrainMcpClient(HttpClient, ILogger)` — `CallToolAsync(string toolName, object? args, CancellationToken)`, `ListToolsAsync(CancellationToken)`; MCP JSON‑RPC to `{BaseUrl}/mcp`, JSON + SSE.
- `GBrainAuthHandler` — bearer token from token file or `GBrainConfig.AuthToken`.
- `IKnowledgeService` / `GBrainKnowledgeService` — `SearchAsync`, `GetPageAsync`, `PutPageAsync`, `DeletePageAsync`; maps GBrain `slug/compiled_truth/score/page_id/metadata` → `RetrievalCandidate`.
- `IScopedKnowledgeService` / `ScopedKnowledgeService` — `RetrieveWithScopeAsync(query, scope, maxResults, sessionId, turnId, ct)`; namespace/scope admission policy.
- DTOs: `RetrievalCandidate { Key, Content, Source, Score, TokenCount, Metadata }`, `KnowledgePage { Key, Content, LastModified, LinkedPages }`.
- DI: `AddLeanKernelKnowledge(GBrainConfig)` in Gateway — `AddHttpClient<GBrainMcpClient>(...).AddHttpMessageHandler<GBrainAuthHandler>()` and `services.AddScoped<IMemoryClient, GBrainMemoryClient>()`. Config section **`LeanKernel:GBrain`** (`BaseUrl`, `AuthToken`, `TimeoutSeconds`).
- Packages: `Microsoft.Extensions.Http`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options` (10.0.x).

Identity scoping: pass the `MemoryScope` (derived from `IPermit` -> `TenantId`/`UserId`/`ChannelId` -> namespace) into `RetrieveWithScopeAsync`, mirroring the original where `UserId` scopes identity/context and `namespace` scopes retrieval admission, now under an explicit tenant boundary. For anonymous requests, `UserId` is the resolved guest user and `SessionId` remains the extra isolation dimension outside the memory namespace.

---

## 6. Configuration (new)

Add `appsettings.json` + `appsettings.Development.json` to `LeanKernel.Gateway`:

```jsonc
{
  "ConnectionStrings": { "Sqlite": "Data Source=leankernel.db" },   // or SqlServer / Postgres
  "OpenAI":   { "ApiKey": "", "BaseUrl": "https://api.openai.com/v1", "DefaultModel": "gpt-4o-mini" },
  "Agents":   { "RootPath": "agents", "DefaultName": "leankernel",
                "DefaultDescription": "LeanKernel assistant",
                "DefaultInstructions": "You are a helpful AI assistant." },
  "Identity": { "AuthenticationType": "Bearer", "Token": { "Issuer": "", "Audience": "", "SecretKey": "" } },
  "Files": { "RootPath": "./data" },
  "Cors": { "Policy": { "Name": "AllowLocal", "Origins": ["http://localhost:3000"] } },
  "LeanKernel": { "GBrain": { "BaseUrl": "http://localhost:8080", "AuthToken": "", "TimeoutSeconds": 30 } }
}
```

Bind: `OpenAI`, `Agents`, `Identity`, `Files`, and `LeanKernel:GBrain`. Register auth/authorization services, the tenant/user/channel-aware permit/resolution pipeline (which may extend or replace the current `AddPermits()` implementation), and any tenant/channel seeding or lookup configuration from these options. Secrets via user-secrets/env in dev; never commit keys (`Directory.Build.props` already handles NU1903 override, unrelated).

---

## 7. Testing Strategy (Requirement #3)

Create three projects under `test/` (matching original conventions), and a `LeanKernel.sln` tying `src` + `test` together.

### 7.1 Unit — `test/LeanKernel.Tests.Unit`

- Packages: `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4, `Microsoft.NET.Test.Sdk` 17.14.1, `FluentAssertions` 8.3.0, `Moq` 4.20.72, `Microsoft.EntityFrameworkCore.InMemory` 10.0.4 (or `Sqlite` in‑memory for relational fidelity), `coverlet.collector` 6.0.4. `[assembly: CollectionBehavior(DisableTestParallelization = true)]`.
- Coverage:
  - `IdentityIsolationKeyProvider` — key = `"{tenantId}|{channelId}|{userId}"` for authenticated users and `"{tenantId}|{channelId}|{userId}|{sessionId}"` for anonymous users.
  - `IPermit`/request-context wiring — request host/principal resolve to `TenantId`, non-null `UserId`, and `ChannelId` plus anonymous guest-user/session behavior.
  - User resolution — non-GUID `sub`/`NameIdentifier` values map to persisted `UserEntity.Id` rather than `Guid.Empty`.
  - Tenant resolution — forwarded/original host values normalize to a single tenant key.
  - `DbChatHistoryProvider` — `Provide` returns only current user's turns in timestamp order; **cross-user/tenant/channel isolation** passes; role mapping; `Store` writes correct rows; message filters honored.
  - `MemoryProvider` — builds query from `AIContext.Messages`; scope carries identity; maps candidates → context; empty‑result path.
  - `GBrainMemoryClient` — maps `RetrievalCandidate` → `MemoryItem` (reuse original `GBrainKnowledgeServiceTests` shape).
  - `DbAgentSessionStore` — round‑trip serialize/deserialize; isolation‑scoped conversation ids reject cross‑isolation reads (`Strict = true`).
  - Interceptors — `IAuditable`/`IRecyclable` behavior with a stub `IPermit`.

### 7.2 Integration — `test/LeanKernel.Tests.Integration`

- Packages: `Microsoft.AspNetCore.Mvc.Testing` 10.0.7, `xunit` 2.9.3, `Microsoft.NET.Test.Sdk` 17.14.1, `coverlet.collector` 6.0.4.
- `GatewayTestApplicationFactory : WebApplicationFactory<Program>` — `UseEnvironment("Testing")`, `AddInMemoryCollection` config (SQLite in‑memory connection, fake OpenAI `BaseUrl`), and `ConfigureServices` to `RemoveAll` + replace the `IChatClient` with a deterministic stub and `IMemoryClient`/`IKnowledgeService` with a stub. Requires `public partial class Program;` (D12).
- Coverage:
  - `POST /v1/responses` returns a well‑formed OpenAI Responses payload for the stubbed agent.
  - History **persists** across two requests on the same conversation id (turn count grows).
  - **Partitioning**: two principals get disjoint history within the same tenant/channel; the same principal in two tenants also gets disjoint history; principal B cannot resume principal A's conversation id (403/empty under `Strict`).
  - Reverse-proxy tenant resolution: forwarded host is honored once `UseForwardedHeaders()` is applied.
  - Non-GUID authenticated subjects (`sub = "user-123"`) resolve to stable persisted `UserEntity` identities across requests.
  - `POST /v1/conversations` create/list happy path.
  - Health/readiness (add a `/health` endpoint if not present).

### 7.3 Playwright API — `test/LeanKernel.Tests.Playwright`

- Packages: `Microsoft.Playwright` 1.52.0, `xunit` 2.9.3, `Microsoft.NET.Test.Sdk` 17.14.1, `xunit.runner.visualstudio` 3.1.4.
- `PlaywrightFixture : IAsyncLifetime` + `[CollectionDefinition(DisableParallelization = true)]`; base URL from `LEANKERNEL_BASE_URL` (default `http://localhost:5080`). Use Playwright's `APIRequestContext` (these are **API** endpoint tests, not UI).
- Coverage:
  - Boot the Gateway (Testing env with SQLite + stub chat client), then exercise `/v1/responses` and `/v1/conversations` over HTTP via `IAPIRequestContext`.
  - Auth header → identity → verify a second identity cannot read the first's conversation.
  - Conversation APIs return external conversation ids only; no scoped/isolation-prefixed ids leak in payloads.
  - DevUI reachable at `/devui` in Development.

---

## 8. Implementation Checklist

> Ordered by dependency. Each item is independently verifiable. `[ ]` = todo.
>
> Status below reflects the current implementation state in this worktree. Items remain unchecked unless the full item is clearly implemented end-to-end.

### Phase 0 — Solution & build scaffolding

- [ ] Expand the existing `src/LeanKernel.sln` to reference the 3 test projects as well as the 4 src projects; it currently includes only the src projects.
- [x] Add `public partial class Program;` at the end of `Programs.cs` (D12). *(Consider renaming `Programs.cs` → `Program.cs` for convention.)*
- [x] Finish configuration wiring: `appsettings*.json` now exist and `OpenAI`/`Agents`/`Identity`/`Files`/`LeanKernel:GBrain` are bound; `AddLeanKernelKnowledge` registers GBrain services when configured.
- [x] Fix `Programs.cs` namespace import (`LeanKernel.Requests` → actual namespace or moved Core interface namespace) (B4).
- [x] Call `app.UseForwardedHeaders()` before middleware/components that depend on `Request.Host` (D19).

### Phase 1 — Identity & partitioning (Req #2)

- [x] Replace the placeholder `RequestContextPermit` wiring with real tenant/user/channel resolution via `IIdentityResolver` + `IdentityResolver` (D8, D24, D25).
- [x] Register authentication + authorization services (`AddAuthentication(...)`, `AddAuthorization()`) before middleware, and keep `UseAuthentication()`/`UseAuthorization()` in order (D14).
- [x] Replace null-forgiving `IPrincipal` registration with deterministic anonymous fallback principal (D17).
- [x] Register session prerequisites and middleware (`AddDistributedMemoryCache`, `AddSession`, `UseSession`) for anonymous isolation fallback (D18, D23).
- [ ] Move or isolate `IPrincipalAccessor`/`IHostNameAccessor` so Logic/Data stop depending on Gateway request abstractions; current permit/tests still reference `LeanKernel.Gateway.Requests` types directly (D10, D24).
- [x] Normalize forwarded host names and resolve `TenantEntity` from them; `UseForwardedHeaders()` is in place and `IdentityResolver.ResolveTenantAsync` performs the lookup (D19, D25).
- [x] Resolve authenticated principals to persisted `UserEntity` rows keyed by issuer/subject; `IdentityResolver.ResolveOrCreateUserAsync` maps non-GUID `sub` values to stable users (D20, D25).
- [x] Resolve or seed a canonical `ChannelEntity` for the OpenAI HTTP surface; `IdentityResolver.ResolveOrCreateChannelAsync` resolves/seeds `ChannelEntity.OpenAiHttpName` (D21, D25).
- [x] Implement `IdentityIsolationKeyProvider : SessionIsolationKeyProvider`; register scoped (§5.2).
- [x] Fix anonymous isolation to include both guest `UserId` and `SessionId`; key format is now `tenant|channel|userId|sessionId` (D15).
- [x] Expand unit coverage from shape tests to real resolution behavior: tenant lookup, persisted user mapping, channel resolution, and anonymous guest-user fallback.

### Phase 2 — Persistence model (G4)

- [x] `TurnEntity`: add `AuthorName` (B2); align role casing convention.
- [x] `SessionEntity`: persist `TenantId`, non-null `UserId`, `ChannelId`, and external `ConversationId`; add unique indexes.
- [x] New `AgentSessionEntity` (§5.8) with optimistic concurrency token.
- [x] Add `EntityContext(DbContextOptions<EntityContext>)` constructor (D13).
- [x] Fix `AddEntityContext` to use `AddDbContext((sp, options) => ...)` and resolve interceptors from real DI (remove `BuildServiceProvider`) (D5b).
- [x] Refactor interceptors to constructor-injected `IPermit` (or supported equivalent) instead of `eventData.Context.GetService<IPermit>()` (D8b).
- [ ] Align the persisted key model: `SessionEntity.Id` is currently `Guid` while `TurnEntity.SessionId` is `string`; define the conversion/migration strategy so chat-history queries and state-bag `chatSessionId` usage are unambiguous.
- [ ] Persist the anonymous isolation dimension in the chat-history/session lookup model; the current `SessionEntity` indexes do not include `SessionId`, so two anonymous browser sessions can still collide if they reuse the same external `conversationId`.
- [x] Complete `EntityContext.OnModelCreating`: `TurnEntity.Session` relationship configured with cascade delete; `UserEntity` indexes for `Issuer`/`Subject` added (D9).
- [x] Add EF migration(s); `InitialAgentRuntime` migration created for SQLite.

### Phase 3 — ChatHistoryProvider (Req #1c)

- [x] Make `DbChatHistoryProvider` identity-safe; ownership verified through `SessionEntity` (TenantId/UserId/ChannelId match required).
- [x] Finish `StoreChatHistoryAsync`: creates sessions with correct ownership, verifies ownership on existing sessions, and persists turns with proper state-bag key management.
- [x] Delete/replace the inert `SessionState` (D2) — either remove it or make it a real value-object over explicit state-bag keys (`conversationId`, `chatSessionId`).
- [ ] Add unit tests for cross-user, cross-tenant, and cross-channel isolation plus first-write session creation and state-bag key behavior; current unit coverage does not exercise `DbChatHistoryProvider`.

### Phase 4 — AgentSession store (Req #1b)

- [ ] Replace the current `JsonSerializer`-based `DbAgentSessionStore` implementation with `agent.SerializeSessionAsync`/`DeserializeSessionAsync`, so session persistence uses the framework-owned format instead of assuming `ChatClientAgentSession` JSON shape.
- [x] Register `AgentSessionStore` as `IsolationKeyScopedAgentSessionStore` wrapping `DbAgentSessionStore` with `Strict = true` (§5.3).
- [x] Populate `TenantId`, `UserId`, `ChannelId` on `AgentSessionEntity`; ownership metadata now populated from `IPermit` on create (D24).
- [ ] Define and centralize state-bag keys for external `conversationId` and persisted `chatSessionId`; current key names are local constants in `DbChatHistoryProvider` and are not coordinated with `DbAgentSessionStore` or conversation storage (D22).
- [x] Extend unit tests: ownership metadata persistence test added.

### Phase 5 — GBrain memory (Req #1d, G5)

- [x] Keep the existing memory abstractions in `LeanKernel.Logic`, but add the missing Gateway-side GBrain implementation and registration path; `GBrainMemoryClient : IMemoryClient` is now the real implementation.
- [x] Port GBrain stack (`GBrainMcpClient`, `GBrainAuthHandler`, `GBrainMemoryClient`, `GBrainException`, `GBrainConfig`) into Logic-side services.
- [x] Replace `StubMemoryClient` with a real `GBrainMemoryClient` wired through DI; `MemoryScope`/`MemoryItem` exist, and `GBrainMemoryClient` maps them to GBrain MCP tool calls.
- [x] Finish `MemoryProvider`: it already avoids `AgentSession` constructor injection and builds a tenant/user/channel scope; GBrain client is wired, token-budget admission policy is a future enhancement.
- [x] `AddLeanKernelKnowledge` DI + `LeanKernel:GBrain` config; conditional registration in `Programs.cs`.
- [x] Add unit tests for GBrain mapping/scoping and keep the current stub-only tests as a fallback-path check.

### Phase 6 — ChatClient, named agent, endpoints (Req #1a, #1e, G6)

- [x] Rename `AddChatClient` → `AddLeanKernelChatClient`; remove `BuildServiceProvider()` (D4, D5); bind `OpenAISettings`.
- [x] Remove legacy `Gateway/AddAgent` registration and replace with `AddAIAgent("leankernel", factory, ServiceLifetime.Scoped)` building `ChatClientAgent` from base-typed providers (D6, D7); use `Instructions`/`ChatOptions`.
- [x] Make the named agent lifetime-safe; agent registered as scoped so providers resolve from request scope (D7).
- [x] Finish `Programs.cs` composition: startup now wires permit/session/providers/chat client/agent/endpoints; GBrain memory client conditionally registered when `LeanKernel:GBrain` is configured.
- [x] `MapOpenAIResponses()`, `MapOpenAIConversations()`; gate `MapDevUI()` on `app.Environment.IsDevelopment()`.
- [ ] EF-backed, tenant/user/channel-scoped `IConversationStorage` + `IAgentConversationIndex` (required for MVP per §5.7).
- [ ] Translate between external `conversationId` and internal `scopedConversationId` inside conversation storage/index so APIs never expose isolation-prefixed ids (D22).
- [x] Add `/health` endpoint.
- [ ] Structured‑output smoke path via `AIAgent.RunAsync<T>`.

### Phase 6.5 — Package/lifetime cleanup

- [x] Add `Microsoft.Agents.AI.Hosting` package to `LeanKernel.Logic` if session store/isolation provider types live there; otherwise keep those types in Gateway (D16).
- [x] Remove duplicate `DbChatHistoryProvider` registrations and dead `AddChatAgent` path (D15).

### Phase 7 — Verification

- [ ] `dotnet build src/LeanKernel.sln` green with warnings‑as‑errors (G1).
- [ ] `dotnet run` Gateway; `curl POST /v1/responses` returns a response; second call on same conversation grows history; DevUI loads.
- [ ] Manual two‑identity partition check.

### Phase 8 — Tests (Req #3)

- [x] Unit project + tests (§7.1).
- [x] Expand the current integration test project beyond `/health`; `GatewayTestApplicationFactory`, `HealthEndpointTests`, `ConversationsEndpointTests`, `ResponsesEndpointTests`, and `AuthenticationEndpointTests` now cover endpoint reachability, request validation, and HTTP method enforcement.
- [x] Playwright API project + fixture + tests (§7.3).
- [x] `dotnet test LeanKernel.sln` green (unit + integration; Playwright gated behind a running server / trait). 46 passing, 2 skipped.

### Phase 9 — Docs

- [ ] Update `README.md` "Repository Structure" to match reality; it still lists many aspirational projects that do not exist in this worktree instead of the actual 4 src + 3 test projects.
- [ ] Add `docs/architecture/agent-runtime.md` describing the provider pipeline + partitioning; link from this PRD.

---

## 9. Risks & Mitigations

| # | Risk | Mitigation |
|---|---|---|
| R1 | **Lifetime mismatch** — singleton named agent vs scoped `EntityContext`/`IPermit`. | Use `IDbContextFactory<EntityContext>` or per-call scopes; fully materialize query results before disposing scope; never capture scoped services in ctor. |
| R2 | Preview/alpha hosting packages (`Hosting`, `Hosting.OpenAI`) may shift APIs. | Pin exact versions in `.csproj`; wrap MAF extension points behind our own thin registration methods; cover with integration tests. |
| R3 | OpenAI conversation storage defaults are in-memory (non-durable, non-partitioned). | Treat EF-backed `IConversationStorage` + `IAgentConversationIndex` as required MVP deliverables. |
| R4 | Reading `sessionId`/user from `AgentSession.StateBag` requires a set‑on‑create convention. | Define a single well‑known state key; set it in the session‑create path; centralize in one helper. |
| R5 | GBrain endpoint unavailable in CI. | `MemoryProvider` degrades gracefully (empty `AIContext`) on GBrain failure; stub `IMemoryClient` in tests. |
| R6 | Anonymous users still need an `AgentSession`. | Resolve a persisted guest user plus ASP.NET session id within the tenant/channel boundary; wire `AddSession`/`UseSession` and deterministic anonymous principal fallback. |
| R7 | Multi‑provider EF migrations (SqlServer/Sqlite/Postgres). | Start with SQLite for dev/tests; generate provider‑specific migrations or use `HasColumnType` guards; document per‑provider `dotnet ef` commands. |
| R8 | Interceptor cannot resolve `IPermit` at save time. | Remove throwaway DI container usage, construct interceptors through real DI, and inject permit dependencies explicitly. |
| R9 | Concurrent writes to same `AgentSession` cause lost updates. | Add optimistic concurrency token (`RowVersion`/provider equivalent) on `AgentSessionEntity` and retry strategy for write conflicts. |
| R10 | Memory injection overflows model context window. | Enforce token budget and top-k selection using retrieval score/token metadata before composing `AIContext`. |
| R11 | Common OIDC subject claims are not GUIDs. | Resolve authenticated requests through persisted `UserEntity` records keyed by issuer + subject; test non-GUID `sub` explicitly. |
| R12 | Reverse-proxy deployments partition on the wrong tenant. | Apply `UseForwardedHeaders()` early and normalize host keys before resolving `TenantEntity`. |
| R13 | Internal scoped conversation IDs leak through API payloads or are confused with persisted chat-session IDs. | Keep external `conversationId`, internal `scopedConversationId`, and persisted `chatSessionId` as separate concepts with explicit translation helpers and tests. |
| R14 | Tenant, user, and channel resolution drift out of sync across layers. | Centralize request-scoped resolution into a single permit/context service reused by providers, session stores, and conversation storage. |
| R15 | Anonymous requests persist inconsistent `UserId` ownership. | Resolve anonymous requests to a canonical guest `UserEntity` per tenant policy and use `SessionId` only as an additional isolation dimension, not a substitute for `UserId`. |

---

## 10. Acceptance Criteria / Definition of Done

- **AC1 (G1):** `dotnet build src/LeanKernel.sln` succeeds with zero warnings/errors under `TreatWarningsAsErrors=true`.
- **AC2 (#1a):** A named `AIAgent` is hosted; `POST /v1/responses` returns a valid OpenAI Responses payload; `RunAsync<T>` structured output verified by a test.
- **AC3 (#1b, #2):** `AgentSession` state persists across restarts and is scoped by `(TenantId, UserId, ChannelId)` for authenticated users and `(TenantId, UserId, SessionId, ChannelId)` for anonymous guest users; cross-identity resume is rejected under `Strict`.
- **AC4 (#1c, #2):** Chat history persists in EF Core and every read/write is filtered by the current canonical identity; cross-user, cross-tenant, and cross-channel isolation tests pass.
- **AC5 (#1d, #2):** `MemoryProvider` injects GBrain‑retrieved, identity‑scoped memory into the prompt; degrades gracefully when GBrain is down.
- **AC6 (#1e):** `/v1/responses` and `/v1/conversations` are mapped and backed by durable EF conversation storage; DevUI available in Development.
- **AC7 (#3):** Unit + integration + Playwright API test projects exist and pass locally (Playwright gated on a running server); partitioning is explicitly tested at each layer.
- **AC8:** No secrets committed; README structure updated.
- **AC9:** Authentication/authorization services plus tenant/user/channel-aware permit wiring are registered in startup (`AddAuthentication`, `AddAuthorization`, and the resolved permit/context pipeline) and verified by integration tests.
- **AC10:** Anonymous requests are partitioned deterministically via a resolved guest user plus ASP.NET session (`AddSession`/`UseSession`) with no null-principal failures and no null `SessionEntity.UserId`.
- **AC11:** Authenticated requests with non-GUID subject claims resolve to persisted `UserEntity.Id` values; no authenticated request path falls back to `Guid.Empty`.
- **AC12:** Reverse-proxy forwarded hosts are honored for tenant resolution, and conversation APIs never expose internal scoped IDs.
- **AC13:** Session, turn, agent-session, and conversation ownership all align on the same canonical `TenantId`, `UserId`, and `ChannelId` partition model.

---

## 11. Decisions Captured

1. **Tenant strategy** — request hostnames resolve to persisted `TenantEntity` rows; `TenantId` is the canonical tenant partition key and hostname is only the lookup input.
1. **User identity strategy** — authenticated principals resolve to persisted `UserEntity` rows keyed by issuer + subject; `UserEntity.Id` is the canonical user partition key.
1. **Anonymous user strategy** — unauthenticated requests also resolve to a persisted guest `UserEntity` under the tenant policy so `SessionEntity.UserId` and other ownership fields stay non-null; `SessionId` remains the extra anonymous isolation dimension.
1. **Channel strategy** — the OpenAI HTTP surface resolves to a canonical `ChannelEntity`; `ChannelEntity.Id` is the canonical channel partition key.
1. **Authentication model** — principals are expected from configured auth; startup must explicitly wire `AddAuthentication(...)` and `AddAuthorization()` before middleware.
1. **Conversation durability** — EF-backed `IConversationStorage` and `IAgentConversationIndex` are required for MVP (not deferred).
1. **Memory layering** — memory abstractions stay in `src/Common/LeanKernel.Logic`; GBrain implementation stays in `src/Services/LeanKernel.Gateway`.
1. **DB strategy** — SQLite for local dev/tests and Postgres for production.
1. **Entity ID strategy for this PRD** — keep existing string IDs on `SessionEntity`/`TurnEntity`; use persisted GUID foreign keys (`TenantId`, `UserId`, `ChannelId`) for ownership, and defer any `Guid`-based `IEntity` unification to a separate migration PRD.
1. **Conversation ID strategy** — keep external OpenAI `conversationId`, internal `scopedConversationId`, and persisted `chatSessionId` as distinct values with explicit translation.

---

## 12. Appendix — Verified MAF API reference (1.13.0)

*Confirmed from the installed assemblies' XML docs; use these exact shapes.*

**`ChatHistoryProvider`** (abstract; ctor takes 3 `Func<IEnumerable<ChatMessage>,IEnumerable<ChatMessage>>` filters):

- `ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(InvokingContext, CancellationToken)`
- `ValueTask StoreChatHistoryAsync(InvokedContext, CancellationToken)`
- `InvokingContext { AIAgent Agent; IEnumerable<ChatMessage> RequestMessages; AgentSession Session; }`
- `InvokedContext { AIAgent Agent; RequestMessages; ResponseMessages; AgentSession Session; Exception? InvokeException; }`

**`AIContextProvider`** (abstract; same 3‑filter ctor):

- `ValueTask<AIContext> ProvideAIContextAsync(InvokingContext, CancellationToken)`
- `ValueTask StoreAIContextAsync(InvokedContext, CancellationToken)`
- `InvokingContext { AIAgent Agent; AIContext AIContext; AgentSession Session; }`
- `AIContext { string? Instructions; IList<ChatMessage> Messages; IList<AITool> Tools; }`

**`AgentSession`** (abstract): `.StateBag` (`AgentSessionStateBag`, get/set typed values); created by `AIAgent.CreateSessionAsync(ct)`; (de)serialized by `AIAgent.SerializeSessionAsync` / `DeserializeSessionAsync`. Extensions: `SetInMemoryChatHistory`, `TryGetInMemoryChatHistory`.

**`AIAgent`**: `CreateSessionAsync(ct)`; `RunAsync(messages, AgentSession, AgentRunOptions?, ct)` (+ overloads); `RunAsync<T>(…)` (**structured output**); `RunStreamingAsync(…)`; `SerializeSessionAsync/DeserializeSessionAsync`.

**`ChatClientAgent`** ctor: `(IChatClient, ChatClientAgentOptions, ILoggerFactory, IServiceProvider)`. `ChatClientAgentOptions`: `Name, Description, Instructions, ChatOptions, ChatHistoryProvider, AIContextProviders, Id, …`.

**Hosting** (`Microsoft.Agents.AI.Hosting`):

- `AddAIAgent(IServiceCollection, string name, Func<IServiceProvider,string,AIAgent> factory, ServiceLifetime)` (+ overloads).
- `AgentSessionStore` (abstract): `GetSessionAsync(AIAgent, string conversationId, ct)`, `SaveSessionAsync(AIAgent, string, AgentSession, ct)`. Impls: `InMemoryAgentSessionStore`, `NoopAgentSessionStore`.
- `SessionIsolationKeyProvider` (abstract): `ValueTask<string> GetSessionIsolationKeyAsync(ct)`.
- `IsolationKeyScopedAgentSessionStore(AgentSessionStore, SessionIsolationKeyProvider, IsolationKeyScopedAgentSessionStoreOptions)`: `GetScopedConversationIdAsync(string, ct)`, `GetIsolationKeyAsync(ct)`. `IsolationKeyScopedAgentSessionStoreOptions { bool Strict }`.

**Hosting.OpenAI** (`Microsoft.Agents.AI.Hosting.OpenAI`):

- Services: `AddOpenAIResponses()`, `AddOpenAIConversations()`, `AddOpenAIChatCompletions()`.
- Endpoints: `MapOpenAIResponses()` / `MapOpenAIResponses(string agentName)` / `MapOpenAIResponses(AIAgent)`; `MapOpenAIConversations()`; `MapOpenAIChatCompletions(...)`.
- Durable extension points: `IConversationStorage`, `IAgentConversationIndex` (defaults `InMemoryConversationStorage`, `InMemoryAgentConversationIndex`).

**DevUI**: `MapDevUI()` / `MapDevUI(string path)`.

## 13. Appendix — Reused GBrain contracts (from original)

- `GBrainMcpClient(HttpClient, ILogger<GBrainMcpClient>)`: `Task<JsonElement?> CallToolAsync(string toolName, object? args = null, CancellationToken)`, `Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(CancellationToken)`. Base address `{BaseUrl}/mcp`.
- `IKnowledgeService`: `Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(string, int=10, ct)`, `Task<KnowledgePage?> GetPageAsync(string, ct)`, `Task PutPageAsync(string, string, ct)`, `Task DeletePageAsync(string, ct)`.
- `IScopedKnowledgeService`: `Task<ScopedRetrievalResult> RetrieveWithScopeAsync(string query, string scope, int maxResults=10, string? sessionId=null, string? turnId=null, ct)`.
- `RetrievalCandidate { string Key; string Content; string Source; double Score; int TokenCount; IReadOnlyDictionary<string,string>? Metadata; }`.
- `KnowledgePage { string Key; string Content; DateTimeOffset LastModified; IReadOnlyList<string> LinkedPages; }`.
- GBrain wire mapping: `slug→Key`, `compiled_truth→Content`, `score→Score`, `page_id→Metadata`. DI: `AddLeanKernelKnowledge(GBrainConfig)`; config `LeanKernel:GBrain:{BaseUrl,AuthToken,TimeoutSeconds}`.
