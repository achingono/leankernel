# Runtime Flows

## Inbound Chat Flow

1. Gateway receives `POST /api/chat`.
2. Session id is resolved or created.
3. `IAgentRuntime.RunTurnAsync` executes turn pipeline.
4. Context, retrieval, tools, and model execution complete.
5. Response is returned with `sessionId`.

## Diagnostics Flow

1. Turn pipeline persists diagnostics snapshots.
2. Gateway diagnostics endpoints project persisted snapshots.
3. UI diagnostics page renders aggregated views.

## Related Pages

- [Gateway API](../api/gateway-api.md)
- [Diagnostics API](../api/diagnostics-api.md)
- [Features: turn pipeline](../features/turn-pipeline.md)

## Source References

- `src/LeanKernel.Gateway/Endpoints.cs`
- `src/LeanKernel.Agents/TurnPipeline.cs`
- `src/LeanKernel.Diagnostics/ContextDiagnosticsService.cs`
