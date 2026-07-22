# Phase 19 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Existing `IPermit` (non-generic) interface | `src/Common/LeanKernel.Core/Interfaces/IPermit.cs` | Repository |
| `RequestContextPermit` implementation | `src/Services/LeanKernel.Gateway/Providers/RequestContextPermit.cs` | Repository |
| `Operation` enum | `src/Common/LeanKernel.Core/Operation.cs` | Repository |
| `IRecyclable` (soft-delete) interface | `src/Common/LeanKernel.Core/Interfaces/IRecyclable.cs` | Repository |
| `IAuditable` interface | `src/Common/LeanKernel.Core/Interfaces/IAuditable.cs` | Repository |
| `EntityContext` with all `DbSet<>` properties | `src/Common/LeanKernel.Data/EntityContext.cs` | Repository |
| Entity class definitions | `src/Common/LeanKernel.Core/Entities/*.cs` | Repository |
| Famorize reference implementation | `~/source/repos/famorize/src/` | Reference project |
| Existing options/configuration binding pattern | `src/Services/LeanKernel.Gateway/Programs.cs` | Repository |
| Existing `IOptions<T>` defaults pattern | `src/Common/LeanKernel.Logic/Providers/ChannelMemoryPolicyResolver.cs:42-44` | Reads from `agentSettings.Value.Channels.MemoryPolicyDefaults` at runtime |

## Optional Inputs
- Famorize test implementations (`MockPermit`, `MockFilter`) for test pattern inspiration.
- Existing ad-hoc query consumers in `src/Common/LeanKernel.Logic/Providers/`, `src/Common/LeanKernel.Logic/Telemetry/`, `src/Services/LeanKernel.Gateway/Sessions/`.

## Input Validation Checklist
- [x] `IPermit` exists with the 8 partitioning properties.
- [x] `Operation` enum covers `Create`, `Read`, `Update`, `Delete`.
- [x] `IRecyclable` defines `IsDeleted` property for soft-delete predicates.
- [x] `IAuditable` defines `CreatedOn`, `CreatedBy`, `UpdatedOn`, `UpdatedBy`.
- [x] All 8 in-scope entities possess `public Guid Id { get; set; }`; 5 entities require `: IEntity` class header addition in Activity 1a.
- [x] `EntityContext` has all required `DbSet<>` members.
- [x] Entity ownership map is documented (see `activities.md`).
- [x] All required famorize reference files are accessible.

