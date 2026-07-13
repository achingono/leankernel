# Data and Persistence

LeanKernel currently persists three different kinds of state:

- transcript sessions and turns
- request-partition ownership metadata
- durable agent runtime state blobs

## EntityContext

The EF Core model lives in [`../../src/Common/LeanKernel.Data/EntityContext.cs`](../../src/Common/LeanKernel.Data/EntityContext.cs).

Primary entity sets:

- `Tenants`
- `Users`
- `Channels`
- `Sessions`
- `Turns`
- `AgentStates`

## Partitioning Model

The current runtime is partitioned by persisted identities:

- tenant
- user
- channel
- anonymous session id as an extra isolation dimension for guest flows

This partition is enforced through `IPermit` resolution and then applied in chat history, session state, and memory access.

Code anchors:

- [`../../src/Common/LeanKernel.Core/Interfaces/IPermit.cs`](../../src/Common/LeanKernel.Core/Interfaces/IPermit.cs)
- [`../../src/Services/LeanKernel.Gateway/Providers/RequestContextPermit.cs`](../../src/Services/LeanKernel.Gateway/Providers/RequestContextPermit.cs)
- [`../../src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs`](../../src/Common/LeanKernel.Logic/Providers/DbChatHistoryProvider.cs)

## Transcript Persistence

`SessionEntity` and `TurnEntity` store transcript history.

- `SessionEntity` owns tenant, user, channel, and optional external `ConversationId`
- `TurnEntity` stores individual chat messages with role, content, author name, and timestamp

Chat history is read and written through `DbChatHistoryProvider`.

## Agent Runtime State

`AgentStateEntity` stores serialized MAF session state keyed by scoped conversation id.

This is intentionally separate from transcript history.

Persistence path:

- `DbAgentStateStore` in Gateway serializes/deserializes MAF session state
- `EntityContext.AgentStates` stores the blob plus tenant/user/channel ownership metadata

## Database Provider Resolution

Provider selection is currently ordered as:

1. `Postgres`
2. `SqlServer`
3. `Sqlite`

Reference: [`../../src/Services/LeanKernel.Gateway/Extensions/DbContextOptionsBuilderExtensions.cs`](../../src/Services/LeanKernel.Gateway/Extensions/DbContextOptionsBuilderExtensions.cs)

In practice:

- direct local development defaults to SQLite from `appsettings*.json`
- Docker Compose overrides runtime persistence to PostgreSQL
