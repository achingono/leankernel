# Runtime Flows

This page summarizes the main request and persistence flows in the current runtime.

## Inbound Request Flow

1. `LeanKernel.Gateway` starts the ASP.NET pipeline.
2. Forwarded headers are applied.
3. Session middleware runs for anonymous isolation support.
4. Authentication and authorization middleware run.
5. OpenAI-compatible endpoints are mapped through MAF hosting.

Reference: [`../../src/Services/LeanKernel.Gateway/Programs.cs`](../../src/Services/LeanKernel.Gateway/Programs.cs)

## Identity Resolution Flow

1. `RequestContextPermit` reads host, principal, and ASP.NET session.
2. `IIdentityResolver` resolves or creates:
   - tenant from host
   - user from claims or guest fallback
   - OpenAI HTTP channel
3. The resolved `IPermit` supplies tenant/user/channel IDs to downstream runtime components.

References:

- [`../../src/Services/LeanKernel.Gateway/Providers/RequestContextPermit.cs`](../../src/Services/LeanKernel.Gateway/Providers/RequestContextPermit.cs)
- [`../../src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs`](../../src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs)

## Chat History Flow

1. The active MAF session is inspected for `chatSessionId`.
2. `DbChatHistoryProvider` verifies the transcript session belongs to the current permit partition.
3. Existing turns are replayed into chat history.
4. New request and assistant messages are persisted back as `TurnEntity` rows.

Reference: [`../../src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs`](../../src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs)

## Memory Flow

1. `MemoryProvider` turns current request messages into a search query.
2. `IMemoryClient` retrieves scoped memory candidates.
3. Retrieved pages are compacted into prompt context.
4. After invocation, fact extraction and normalization run.
5. Scope-relative normalized memory pages are persisted back through `IMemoryClient`.

Memory scoping uses the memory pipeline's `MemoryScope` and transport-specific implementation. It does not reuse the agent-session isolation key provider.

Reference: [`../../src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs`](../../src/Common/LeanKernel.Logic/Providers/MemoryProvider.cs)

## Agent State Flow

1. The MAF conversation id is scoped through `IdentityIsolationKeyProvider`.
2. `DbAgentStateStore` loads or creates the runtime session.
3. Serialized session state is stored in `AgentStateEntity` for future resumption.

This flow is intentionally separate from durable memory scope so transcript/session continuity can evolve independently from long-term memory behavior.

References:

- [`../../src/Services/LeanKernel.Gateway/Providers/IdentityIsolationKeyProvider.cs`](../../src/Services/LeanKernel.Gateway/Providers/IdentityIsolationKeyProvider.cs)
- [`../../src/Services/LeanKernel.Gateway/Sessions/DbAgentStateStore.cs`](../../src/Services/LeanKernel.Gateway/Sessions/DbAgentStateStore.cs)
