# PRD: Phase 2 Context Diagnostics API

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and implementers
- **Phase goal:** Expose the per-turn context assembly decisions through authenticated diagnostics APIs backed by persisted turn-time snapshots.
- **Plan review:** Reviewed by `gpt-4.1`. Review outcome: proceed with this slice, keep the new routes in `src/LeanKernel.Gateway/Endpoints.cs`, use the existing `IDiagnosticsSink.RecordAsync/GetEntriesAsync` contract, create the turn identifier before context assembly so retrieval and stored snapshots share the same turn id, bind the new diagnostics settings from `DiagnosticsConfig`, and log or skip malformed legacy diagnostic payloads without failing the API surface.

## Problem statement

LeanKernel already records individual diagnostic artifacts, but operators still cannot retrieve a coherent audit of what context was considered, admitted, budgeted, or shaped for a specific turn. Phase 2 needs a persisted snapshot captured during context assembly so diagnostics endpoints can report exactly what happened without recomputing from mutable session state.

## Scope

This task will:

1. Add API response models for full context, budget, and history diagnostics under `src/LeanKernel.Abstractions/Models/`.
2. Add `IContextDiagnosticsService` and `ContextDiagnosticsSnapshot` under `src/LeanKernel.Abstractions/Interfaces/`.
3. Align `ContextAdmissionRecord` with the Phase 2 contract used by the requested API responses and update direct consumers/tests accordingly.
4. Extend `DiagnosticsConfig` with `ContextDiagnosticsEnabled` and `MaxDiagnosticsPerSession`.
5. Add a dedicated diagnostics category for persisted context snapshots.
6. Implement `ContextDiagnosticsService` in `src/LeanKernel.Diagnostics/` using the existing `IDiagnosticsSink` contract and JSON payload persistence.
7. Update `TurnPipeline` so a stable `turnId` exists before context assembly and is used for retrieval diagnostics, stored snapshots, assistant turn events, and endpoint filtering.
8. Extend `src/LeanKernel.Gateway/Endpoints.cs` with authenticated `/api/diagnostics/{sessionId}/context`, `/budget`, and `/history` routes with optional `turnId` query filtering and structured 404 responses.
9. Register the new diagnostics service in DI and add the new diagnostics settings to `src/LeanKernel.Gateway/appsettings.json`.
10. Add unit and integration coverage for storage/retrieval behavior and the new endpoint surface.
11. Attempt repository validation commands and quality scripts, recording the local `dotnet` blocker if it persists.

## Out of scope

- Recomputing context diagnostics from session history instead of using persisted turn snapshots.
- Changing the existing basic `/api/diagnostics/{sessionId}` endpoint contract beyond preserving current behavior.
- Adding new persistence stores or tables beyond the current diagnostics sink mechanism.
- Building UI/operator-console screens for browsing context diagnostics.

## Functional requirements

### FR-1 Persisted turn snapshot

- Capture diagnostics after context assembly and tool visibility resolution, before the turn completes.
- Persist a single snapshot per turn containing admissions, budget usage, the effective budget allocation, history shaping diagnostics, retrieval diagnostics, and a timestamp.
- Use the same `turnId` across retrieval diagnostics, stored snapshots, assistant turn events, and endpoint filtering.

### FR-2 Context audit endpoint

- `GET /api/diagnostics/{sessionId}/context` returns the latest snapshot for the session unless `?turnId=` is supplied.
- Response includes admissions, counts for considered/admitted/excluded candidates, timestamp, and retrieval diagnostics.
- Return `404` with a structured error payload when no snapshot is available for the requested session or turn.

### FR-3 Budget diagnostics endpoint

- `GET /api/diagnostics/{sessionId}/budget` returns budget totals, usable tokens, response headroom ratio, category-level allocations, and actual usage.
- Each budget category includes allocated, used, and computed utilization.

### FR-4 History diagnostics endpoint

- `GET /api/diagnostics/{sessionId}/history` returns the stored history shaping diagnostics for the turn, including verbatim/compacted/summarized/dropped counts and tokens saved.
- When history shaping diagnostics were unavailable for that turn, return the response with `Shaping = null` and derived counts/tokens based on the stored snapshot.

### FR-5 Safety and backward compatibility

- All new endpoints must use the same API key validation as the existing diagnostics endpoint.
- Invalid or legacy diagnostic payloads must be skipped rather than crashing the service.
- Existing diagnostics collection and persistence behavior must remain intact for unrelated categories.

## Design notes

### Contracts

- Add the requested response records in `LeanKernel.Abstractions.Models` using file-scoped namespaces and nullable-aware defaults.
- Update `ContextAdmissionRecord` to use `Key`, `Source`, `Score`, `TokenCount`, `Admitted`, and `ExclusionReason` so persisted payloads and API responses match the Phase 2 contract.
- Add a dedicated `DiagnosticCategory.ContextSnapshot` enum member for persisted context snapshots.

### Service behavior

- `ContextDiagnosticsService` writes snapshots as `DiagnosticEntry` payload JSON through `IDiagnosticsSink.RecordAsync`.
- Reads use `IDiagnosticsSink.GetEntriesAsync(sessionId)` then filter to context snapshot entries, deserialize, skip malformed payloads, order by timestamp, cap work with `MaxDiagnosticsPerSession`, and return either the requested turn or latest snapshot.
- When `ContextDiagnosticsEnabled` is false, snapshot writes are skipped and read APIs behave as not found.

### Pipeline integration

- `TurnPipeline` creates a `turnId` before calling the gatekeeper and adds it to message metadata so retrieval diagnostics can inherit the same id.
- After merging visible tools into the context, `TurnPipeline` stores a `ContextDiagnosticsSnapshot` built from the gated context, active budget, and current timestamp.
- `TurnPipeline` continues to publish the assistant `TurnEvent` using that same `turnId`.

### Gateway behavior

- Keep the endpoint implementation in `src/LeanKernel.Gateway/Endpoints.cs` to match the current composition style.
- Preserve the existing raw diagnostics route.
- New routes return `401` when unauthorized, `404` with `{ error = ... }` when no context diagnostics are available, and `200` with the typed payload otherwise.

### Configuration and testing

- Extend `DiagnosticsConfig` and bind the new properties through the existing `LeanKernelConfig` binding in `Program.cs`.
- Update gateway integration tests to inject a stub `IContextDiagnosticsService` alongside any existing diagnostics sink stubs.
- Add unit coverage for latest-turn selection, explicit turn selection, disabled configuration, malformed payload skipping, and capped entry evaluation.
- Extend pipeline tests to verify early `turnId` propagation and snapshot persistence.

## Validation plan

1. Review the resulting diff for contract, namespace, and DI consistency.
2. Attempt `dotnet restore src/LeanKernel.sln`.
3. Attempt `dotnet build src/LeanKernel.sln --no-restore -v minimal`.
4. Attempt `dotnet test src/LeanKernel.sln --no-build -v minimal`.
5. Attempt `scripts/quality/test-coverage.sh`.
6. Attempt `scripts/quality/sonarqube-scan.sh`.
7. If `dotnet` remains unavailable, record the blocker explicitly and still perform source-level verification of the changed files.

## Acceptance criteria

- Context diagnostics response models and service contracts are added in the abstractions project.
- A diagnostics service persists and retrieves per-turn context snapshots through `IDiagnosticsSink`.
- `TurnPipeline` stores a context snapshot with a stable turn id created before context assembly.
- Gateway exposes authenticated `/context`, `/budget`, and `/history` diagnostics routes with optional `turnId` filtering and structured 404 responses.
- Diagnostics DI registration and appsettings include the new context diagnostics settings.
- Unit and integration tests cover the new service and endpoint behaviors.
- Validation evidence records the local `dotnet` blocker if it persists.
