# Phase 1 LeanKernel.Persistence PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and implementers
- **Phase goal:** Deliver the Postgres-backed persistence layer for the rearchitecture so runtime services can durably store sessions, conversation history, and diagnostic entries behind the `LeanKernel.Abstractions` contracts.
- **Plan review:** Reviewed by `claude-sonnet-4.6`. Review outcome: proceed with the requested entity surface, document `CapabilityGapEntity` as schema-forward for future work, keep session history ordering deterministic by using `Timestamp` plus `Id` as a tiebreaker, and record toolchain blockers if local .NET validation is unavailable.

## Problem statement

`LeanKernel.Persistence` is still a scaffold project with placeholder files. Phase 1 needs a concrete EF Core implementation that can back the existing abstraction contracts with PostgreSQL storage and DI registration.

## Scope

This task will:

1. Implement the requested EF Core entities under `src/LeanKernel.Persistence/Entities`.
2. Implement `LeanKernelDbContext` with the requested DbSets, indexes, and relationships.
3. Implement `PostgresSessionStore` for `ISessionStore`.
4. Implement `PostgresDiagnosticsSink` for `IDiagnosticsSink`.
5. Implement `PersistenceServiceCollectionExtensions.AddLeanKernelPersistence(DatabaseConfig config)`.
6. Remove placeholder files from `LeanKernel.Persistence`.
7. Update directly coupled smoke tests that still reference the removed persistence marker type.
8. Attempt build and quality validation, recording environment blockers when the local toolchain is unavailable.

## Out of scope

- Adding migrations or applying schema changes to a live database.
- Introducing repositories or additional abstraction layers beyond the requested contracts.
- Adding a capability-gap service interface; `CapabilityGapEntity` is included only as requested schema-forward persistence scaffolding for later tasks.
- Expanding persistence APIs beyond `ISessionStore` and `IDiagnosticsSink`.

## Architecture and data model

### Entities

- `SessionEntity`
  - string primary key
  - uniquely indexed by `(ChannelId, UserId)` for `GetOrCreateSessionIdAsync`
  - owns many `TurnEntity` rows
  - optional `Metadata` stores JSON text without task-specific parsing logic
- `TurnEntity`
  - string primary key
  - required `SessionId`, `Role`, and `Content`
  - indexed by `SessionId` and `Timestamp`
  - stores compaction metadata from `ConversationTurn`
- `CapabilityGapEntity`
  - persisted for future learning/analysis workflows
  - no service interface wiring in this task
- `DiagnosticEntryEntity`
  - string primary key aligned to `DiagnosticEntry.Id`
  - stores serialized JSON payload text and timestamps for later inspection

### DbContext design

`LeanKernelDbContext` will expose DbSets for all four entities and configure:

- primary keys for each entity
- `SessionEntity -> TurnEntity` one-to-many with FK `SessionId`
- indexes matching the requested hot paths
- explicit use of the requested entity shapes without adding additional columns

## Service design

### `PostgresSessionStore`

- Use `IDbContextFactory<LeanKernelDbContext>` so each operation creates and disposes its own context.
- Use primary constructor dependency injection with argument validation in field initializers.
- `GetOrCreateSessionIdAsync`
  - fetch the most recently updated session for a `(channelId, userId)` pair
  - update `UpdatedAt` when reusing an existing session
  - create and log a new session otherwise
- `AppendTurnAsync`
  - map `ConversationTurn` to `TurnEntity`
  - update the parent session `UpdatedAt` when a turn is appended
- `GetHistoryAsync`
  - fetch the newest `maxTurns` by `Timestamp` descending with `Id` as a deterministic tiebreaker
  - return results in chronological order for callers

### `PostgresDiagnosticsSink`

- Use `IDbContextFactory<LeanKernelDbContext>` with per-operation contexts.
- Serialize `DiagnosticEntry.Payload` with `System.Text.Json` into the entity `Payload` string column.
- Deserialize payloads back to `object`; callers should expect JSON-backed values such as `JsonElement`.

### DI registration

- Register `LeanKernelDbContext` via `AddDbContextFactory` using `DatabaseConfig.ConnectionString` and `UseNpgsql`.
- Register `ISessionStore` as scoped.
- Register `IDiagnosticsSink` as singleton so it can be safely consumed by the existing singleton diagnostics collector.

## Cleanup plan

- Delete `src/LeanKernel.Persistence/AssemblyMarker.cs`.
- Delete `src/LeanKernel.Persistence/GlobalUsings.cs` because SDK implicit usings are already enabled in the project.
- Update `src/LeanKernel.Tests.Unit/ScaffoldSmokeTests.cs` to anchor persistence smoke coverage on `typeof(LeanKernel.Persistence.LeanKernelDbContext).Assembly.GetName().Name`.

## Validation plan

1. Inspect the resulting file tree for the expected entities, DbContext, store, sink, and DI extension.
2. Attempt `dotnet build src/LeanKernel.Persistence/LeanKernel.Persistence.csproj --no-restore`.
3. Attempt `dotnet build src/LeanKernel.sln --no-restore -v minimal`.
4. Attempt `dotnet test src/LeanKernel.sln --no-build -v minimal`.
5. Attempt `scripts/quality/test-coverage.sh`.
6. Attempt `scripts/quality/sonarqube-scan.sh`.
7. If `dotnet` or other required tooling is unavailable locally, record that blocker and fall back to source-level verification only.

## Acceptance criteria

- `LeanKernel.Persistence` contains the requested EF Core entity types, DbContext, store, diagnostics sink, and DI extension.
- Placeholder files are removed from `LeanKernel.Persistence`.
- The directly coupled smoke test no longer references `LeanKernel.Persistence.AssemblyMarker`.
- Validation evidence is recorded, including local toolchain blockers when applicable.
- SQL todo `p1-persistence` is marked `done` after implementation and validation attempts.
