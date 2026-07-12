# Gap Analysis — PRD: Identity-Partitioned Agent Runtime

## Summary

The PRD is thorough and well-structured. It correctly identifies the 12 build/design defects and proposes a sensible MAF-native architecture. However, cross-referencing it against the actual codebase surfaces **factual inaccuracies, unaddressed architectural concerns, and implementation plan gaps** that would cause problems during execution.

---

## 1. Factual Inaccuracies (PRD vs. Actual Code)

### 1.1 `IPermit` is NOT missing — it already exists, with a generic variant

> [!IMPORTANT]
> **PRD D8** states *"`IPermit` is never registered → every `SaveChanges` throws."*

**Reality**: [`IPermit`](file:///Users/achingono/source/worktrees/leankernel/src/Common/LeanKernel.Core/Interfaces/IPermit.cs) already exists in `LeanKernel.Core` with `Id`, `Badge`, and `HostName` — exactly the shape the PRD proposes for `RequestPermit`. There's also an [`IPermit<TEntity>`](file:///Users/achingono/source/worktrees/leankernel/src/Common/LeanKernel.Core/Interfaces/IPermit.cs#L31-L39) generic variant with `Can(Operation)`.

Furthermore, [`ClaimsPermit<TEntity>`](file:///Users/achingono/source/worktrees/leankernel/src/Services/LeanKernel.Gateway/Requests/ClaimsPermit.cs) already implements `IPermit<TEntity>`, and [`AddPermits()`](file:///Users/achingono/source/worktrees/leankernel/src/Services/LeanKernel.Gateway/Extensions/IServiceCollectionExtensions.cs#L388-L396) already registers it:

```csharp
services.AddScoped(typeof(IPermit<>), typeof(ClaimsPermit<>));
services.AddScoped<IPermit<object>, ClaimsPermit<object>>();
services.TryAddScoped<IPermit>(provider => provider.GetRequiredService<IPermit<object>>());
```

**Gap**: D8 is partially correct — `AddPermits()` is **never called** in `Programs.cs`. The registration method exists but isn't wired. The PRD should say *"IPermit registration is never invoked"* not *"IPermit is never registered"*, and the fix is to call `AddPermits()` rather than creating a new `RequestPermit` class.

### 1.2 `IPrincipalAccessor`/`IHostNameAccessor` namespace mismatch

> **PRD D10** states the interfaces are in namespace `LeanKernel.Requests`.

**Reality**: The actual namespace is `LeanKernel.Gateway.Requests` (see [IPrincipalAccessor.cs](file:///Users/achingono/source/worktrees/leankernel/src/Services/LeanKernel.Gateway/Requests/IPrincipalAccessor.cs#L3)). This is important because `Programs.cs` line 2 does `using LeanKernel.Requests;` — a namespace that **does not exist** — which is itself an additional build error the PRD missed.

### 1.3 `PrincipalAccessor` is registered as **Singleton**, not Scoped

**PRD** implies accessor registrations are fine and only the interfaces need moving. But [Programs.cs:24](file:///Users/achingono/source/worktrees/leankernel/src/Services/LeanKernel.Gateway/Programs.cs#L24) registers:

```csharp
builder.Services.TryAddSingleton<IPrincipalAccessor, PrincipalAccessor>();
```

A singleton `PrincipalAccessor` captures an `IHttpContextAccessor` (which is fine) but the PRD's `IdentityIsolationKeyProvider` and `IPermit` depend on these as scoped. The accessor *implementations* that resolve `HttpContext.User` work correctly as singletons only because they read from `IHttpContextAccessor` each time — but this is subtle and fragile. The PRD never discusses this lifetime consideration.

### 1.4 `EntityContext` has no constructor accepting `DbContextOptions`

[EntityContext.cs](file:///Users/achingono/source/worktrees/leankernel/src/Common/LeanKernel.Data/EntityContext.cs) has **no constructor at all** — it relies on the parameterless `DbContext()` base constructor. The `AddDbContext<EntityContext>` call in [AddEntityContext](file:///Users/achingono/source/worktrees/leankernel/src/Common/LeanKernel.Data/Extensions/IServiceCollectionExtensions.cs#L28) passes options via `DbContextOptionsBuilder`, which requires a `DbContext(DbContextOptions)` constructor. **This is a build error the PRD doesn't list.**

### 1.5 `SessionEntity.Id` is `string`, not `Guid` — IEntity mismatch

[`IEntity`](file:///Users/achingono/source/worktrees/leankernel/src/Common/LeanKernel.Core/Interfaces/IEntity.cs) requires `Guid Id { get; set; }`, but [`SessionEntity`](file:///Users/achingono/source/worktrees/leankernel/src/Common/LeanKernel.Core/Entities/SessionEntity.cs#L11) and [`TurnEntity`](file:///Users/achingono/source/worktrees/leankernel/src/Common/LeanKernel.Core/Entities/TurnEntity.cs#L11) use `string Id`. **Neither entity implements `IEntity`**, so `IAuditable`/`IRecyclable` (which extend `IEntity`) cannot be applied as the PRD suggests (§5.8: *"Optionally implement `IAuditable`, `IRecyclable`"*) without changing the PK type from `string` to `Guid`, which is a breaking migration decision the PRD doesn't address.

---

## 2. Architectural Gaps

### 2.1 `AddEntityContext` has its own `BuildServiceProvider()` antipattern — not mentioned

The PRD calls out `BuildServiceProvider()` in Logic (D5) and Gateway (D5), but [`AddEntityContext`](file:///Users/achingono/source/worktrees/leankernel/src/Common/LeanKernel.Data/Extensions/IServiceCollectionExtensions.cs#L25-L27) does the **same thing**:

```csharp
using var serviceProvider = services.BuildServiceProvider();
using var scope = serviceProvider.CreateScope();
var interceptors = scope.ServiceProvider.GetServices<ISaveChangesInterceptor>();
```

This creates a **throwaway container** at registration time to resolve interceptors that haven't been registered yet (they're registered on line 22-23, the lines immediately above). The resolved interceptor instances will be **different** from the ones the real container uses. This is a critical bug: the interceptors added to `DbContextOptions` are orphan instances with no access to the real DI scope — so `eventData.Context?.GetService<IPermit>()` will return `null`, causing `ArgumentNullException.ThrowIfNull(permit)` to throw.

### 2.2 Interceptors resolve `IPermit` from `DbContext` service provider — wrong resolution path

Both [AuditableInterceptor](file:///Users/achingono/source/worktrees/leankernel/src/Common/LeanKernel.Data/Interceptors/AuditableInterceptor.cs#L48) and [RecyclableInterceptor](file:///Users/achingono/source/worktrees/leankernel/src/Common/LeanKernel.Data/Interceptors/RecyclableInterceptor.cs#L50) resolve `IPermit` via:

```csharp
var permit = eventData.Context?.GetService<IPermit>();
```

`DbContext.GetService<T>()` resolves from the **internal EF service provider**, not the application's DI container — unless you explicitly configure `options.UseInternalServiceProvider(sp)` or register `IPermit` into EF's internal container. The PRD's proposed fix (register `IPermit` in the app DI) will **not** make it available via `GetService<IPermit>()` on the context.

**Correct fix**: Either (a) use `options.UseApplicationServiceProvider()` (EF Core 10 may support this), (b) resolve `IPermit` via `IHttpContextAccessor` inside the interceptor, or (c) inject `IPermit` via interceptor constructor — but that requires the interceptor to be DI-resolved, not pre-instantiated.

### 2.3 Singleton agent ↔ scoped providers: `IServiceScopeFactory` approach has a disposal risk

The PRD correctly identifies the singleton/scoped mismatch (R1) and prescribes `IServiceScopeFactory`. However, it doesn't specify **who disposes the scope**. If `ProvideChatHistoryAsync` creates a scope, that scope (and its `EntityContext`) must be disposed after the query materializes. If the materialized results are lazy (`IEnumerable`), disposing the scope before enumeration will throw. The PRD says *"Materialize (`ToListAsync`)"* but doesn't enforce this as a constraint on all code paths, nor does it address the `StoreChatHistoryAsync` path which may also need its own scope.

### 2.4 `AddAuthentication` references undefined types `IdentitySettings` and `FileSettings`

[`AddAuthentication`](file:///Users/achingono/source/worktrees/leankernel/src/Services/LeanKernel.Gateway/Extensions/IServiceCollectionExtensions.cs#L55-L60) takes `IdentitySettings` and `FileSettings` parameters — **neither class exists in the codebase**. This is an additional build error on top of Logic's errors. `Programs.cs` doesn't call `AddAuthentication()`, so it doesn't surface until auth is wired — but the PRD doesn't mention these missing types at all, and they'll need to be defined before auth works.

### 2.5 Cookie name hardcoded to "Famorize.Auth"

[Line 171 of IServiceCollectionExtensions.cs](file:///Users/achingono/source/worktrees/leankernel/src/Services/LeanKernel.Gateway/Extensions/IServiceCollectionExtensions.cs#L171) has `options.Cookie.Name = "Famorize.Auth"`. This appears to be a leftover from another project. The PRD doesn't mention this.

### 2.6 No `Hosting` package reference in Logic where `AgentSessionStore` would live

The PRD places `DbAgentSessionStore : AgentSessionStore` in `LeanKernel.Logic` (§5.3). But `AgentSessionStore` comes from `Microsoft.Agents.AI.Hosting`, which is only referenced in the Gateway project (as `Microsoft.Agents.AI.Hosting.OpenAI`). The [Logic .csproj](file:///Users/achingono/source/worktrees/leankernel/src/Common/LeanKernel.Logic/LeanKernel.Logic.csproj) references `Microsoft.Agents.AI` (core), `Microsoft.Agents.AI.OpenAI`, and `Microsoft.Agents.AI.Workflows` — none of which include `AgentSessionStore` or `SessionIsolationKeyProvider`.

**Fix**: Either add `Microsoft.Agents.AI.Hosting` to Logic's package references, or move `DbAgentSessionStore` to Gateway.

### 2.7 `MapOpenAIResponses()` (no-arg) vs. `MapOpenAIResponses("leankernel")`

[Programs.cs:92](file:///Users/achingono/source/worktrees/leankernel/src/Services/LeanKernel.Gateway/Programs.cs#L92) calls `app.MapOpenAIResponses()` (no agent name). The PRD says to change this to `app.MapOpenAIResponses("leankernel")` to route to the named agent. However, the PRD also notes the agent is registered via `AddAIAgent("leankernel", factory)`, but the current code uses `TryAddScoped<AIAgent>(...)` (unnamed). The PRD correctly identifies this as D7, but the implementation plan doesn't address that `AddAIAgent` and `TryAddScoped<AIAgent>` live in **different extension method classes** — the plan needs to explicitly call out removing the old `AddAgent` method in the Gateway extensions.

---

## 3. Implementation Plan Gaps

### 3.1 No handling of `AddEntityContext`'s `BuildServiceProvider()` antipattern

The PRD calls out D5 for `Logic/AddChatClient` and `Gateway/AddAgent`, but misses the identical pattern in [`Data/AddEntityContext`](file:///Users/achingono/source/worktrees/leankernel/src/Common/LeanKernel.Data/Extensions/IServiceCollectionExtensions.cs#L25). This should be fixed in Phase 0 or Phase 2 by using the `AddDbContext` overload that accepts `(sp, options) =>` to resolve interceptors from the real container.

### 3.2 No migration strategy for `string Id` → `Guid Id` (if implementing `IAuditable`/`IRecyclable`)

The PRD suggests entities *"Optionally implement `IAuditable`, `IRecyclable`"* but doesn't address the `string` vs `Guid` PK type conflict with `IEntity`. This needs an explicit decision:
- Keep `string Id` and skip `IEntity`/`IAuditable`/`IRecyclable` on session/turn entities, or
- Change to `Guid Id` (breaking change to any existing data), or
- Remove `IEntity` from the `IAuditable`/`IRecyclable` inheritance chain.

### 3.3 Phase ordering: `IPermit` registration (Phase 1) before `EntityContext` fix (Phase 2)

Phase 1 registers `IPermit`, but the interceptors that consume it won't work until the `EntityContext`/interceptor resolution is fixed (§2.1, §2.2). These fixes aren't in any phase. Recommend adding an explicit Phase 0.5 or merging into Phase 2.

### 3.4 `Programs.cs` line 2 — `using LeanKernel.Requests;` doesn't resolve

This is a **build error not listed in §4.2**. The namespace is `LeanKernel.Gateway.Requests` (or will become `LeanKernel` after moving to Core per D10). The implementation checklist should call this out explicitly.

### 3.5 `Programs.cs` line 95 — `builder.Environment` vs `app.Environment`

The PRD notes (§5.7): *"gate on `app.Environment`, not `builder.Environment`"*. [Line 95](file:///Users/achingono/source/worktrees/leankernel/src/Services/LeanKernel.Gateway/Programs.cs#L95) uses `builder.Environment.IsDevelopment()` after `app` has been built. While `builder.Environment` and `app.Environment` reference the same underlying object in the default `WebApplicationBuilder`, the PRD's own guidance contradicts the current code. However, this isn't listed in the numbered defects (B1–B3, D1–D12) and could be lost.

### 3.6 `AddChatClient` double-registration

[Logic extensions](file:///Users/achingono/source/worktrees/leankernel/src/Common/LeanKernel.Logic/Extensions/IServiceCollectionExtensions.cs#L15-L26) registers `DbChatHistoryProvider` **twice** — once in `AddContextProviders()` and again in `AddChatHistoryProviders()`. Both are called from [Programs.cs:66-67](file:///Users/achingono/source/worktrees/leankernel/src/Services/LeanKernel.Gateway/Programs.cs#L66-L67). The PRD doesn't call out this redundancy.

### 3.7 Missing `Hosting` package in Logic project not flagged

As noted in §2.6, `DbAgentSessionStore`/`IdentityIsolationKeyProvider`/`SessionIsolationKeyProvider` require the `Microsoft.Agents.AI.Hosting` package, which isn't in `LeanKernel.Logic.csproj`. The implementation checklist doesn't include adding this package reference.

### 3.8 Open Question #3 answer contradicts Phase 6b

Open Question #3 answer says *"Conversations are stored via Entity Framework"* — making durable `IConversationStorage`/`IAgentConversationIndex` a **requirement**, not optional. But Phase 6b still marks it as *"(6b, optional/durable)"*. The checklist should promote this to a required deliverable.

### 3.9 Open Question #4 answer contradicts §5.10

Open Question #4 says *"Abstract memory logic in `LeanKernel.Logic` with GBrain implementation in `LeanKernel.Gateway`"*. But §5.10 still proposes *"a new `src/Common/LeanKernel.Knowledge` classlib, or fold into `LeanKernel.Logic`"* and Phase 5 references `LeanKernel.Knowledge`. The architecture section and the implementation checklist need updating to match the answer.

### 3.10 No plan for `IdentitySettings`/`FileSettings` types

The `AddAuthentication` extension method requires these types. If auth is needed for identity partitioning to work (which it is — `ClaimsPermit` depends on authenticated claims), these types need to be defined. The PRD's open question #2 confirms *"Principals arrive via configured auth"* but doesn't mention these missing types.

---

## 4. Risks & Mitigations Gaps

### 4.1 R1 is under-specified

> *"Providers resolve a fresh scope per invocation via `IServiceScopeFactory`"*

This doesn't address:
- **Scope disposal timing** relative to materialization (see §2.3)
- Whether `StoreChatHistoryAsync` and `ProvideChatHistoryAsync` share a scope or get separate ones (relevant for transaction consistency)
- What happens if the `AgentSession` is modified by both provider and store in the same request — are they operating on the same `EntityContext` instance?

### 4.2 No risk entry for interceptor/IPermit resolution failure

The `GetService<IPermit>()` resolution path from `DbContext` internals (§2.2) is a hard-to-debug runtime failure. This deserves its own risk entry.

### 4.3 No concurrency/race-condition risk for `AgentSession`

If two requests for the same user arrive concurrently, `DbAgentSessionStore` could encounter write conflicts on `AgentSessionEntity`. The PRD doesn't mention optimistic concurrency (`RowVersion`/`xmin`) or serialization strategy.

### 4.4 No risk for `MemoryProvider` token budget

The PRD's `MemoryProvider` injects all retrieved memories as a single `ChatMessage` without any token budget consideration. If GBrain returns many/large candidates, the prompt could exceed the model's context window. The original project's `ScopedKnowledgeService` has `TokenCount` on `RetrievalCandidate` — but the PRD doesn't describe using it for admission gating.

---

## 5. Minor Issues

| # | Issue | Location |
|---|-------|----------|
| M1 | PRD references `LeanKernel.Requests` namespace; actual is `LeanKernel.Gateway.Requests` | §5.2, §4.2 |
| M2 | PRD says `RequestPermit : IPermit` should be created; `ClaimsPermit` already exists and fulfills this role | §5.2 |
| M3 | Cookie name "Famorize.Auth" is a leftover from another project | [Line 171](file:///Users/achingono/source/worktrees/leankernel/src/Services/LeanKernel.Gateway/Extensions/IServiceCollectionExtensions.cs#L171) |
| M4 | `AddChatAgent` in Logic extensions registers `ChatClientAgent` as scoped — dead code not mentioned | [Line 42-46](file:///Users/achingono/source/worktrees/leankernel/src/Common/LeanKernel.Logic/Extensions/IServiceCollectionExtensions.cs#L42-L46) |
| M5 | `MemoryProvider` calls `_memoryClient.LoadMemoriesAsync()` with `x.Text` — `ChatMessage.Text` may be null for multi-content messages | [Line 31](file:///Users/achingono/source/worktrees/leankernel/src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs#L31) |
| M6 | `Terminals/` directory exists but is empty — PRD doesn't mention cleaning it up | Project structure |
| M7 | `Operation.cs` in Core exists but isn't discussed in the PRD | [Operation.cs](file:///Users/achingono/source/worktrees/leankernel/src/Common/LeanKernel.Core/Operation.cs) |

---

## Recommendations

1. **Update defect list**: Add the missing `EntityContext` constructor, `using LeanKernel.Requests` namespace, `AddEntityContext`'s `BuildServiceProvider()`, and `IdentitySettings`/`FileSettings` absence to the enumerated defects.
2. **Reconcile IPermit**: Use the existing `ClaimsPermit` + `AddPermits()` rather than creating a new `RequestPermit`. Just wire `AddPermits()` into `Programs.cs`.
3. **Fix interceptor resolution**: Address the `GetService<IPermit>()` anti-pattern in interceptors before Phase 1 — this is foundational.
4. **Decide on entity PK type**: `string` vs `Guid` for `SessionEntity`/`TurnEntity` vs `IEntity.Id` — this blocks any `IAuditable`/`IRecyclable` implementation.
5. **Update PRD to match open question answers**: Phase 6b should be required; §5.10 should describe Logic+Gateway split, not `LeanKernel.Knowledge`.
6. **Add `Microsoft.Agents.AI.Hosting` package**: To `LeanKernel.Logic.csproj` or relocate session store types.
7. **Add concurrency controls**: `RowVersion` on `AgentSessionEntity` for optimistic concurrency.
8. **Token budget gating**: Use `RetrievalCandidate.TokenCount` in `MemoryProvider` to cap injected context.
