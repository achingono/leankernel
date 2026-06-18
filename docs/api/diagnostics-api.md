# Diagnostics API

The diagnostics API exposes persisted turn-time evidence from context assembly and history shaping.

## Endpoints

- `GET /api/diagnostics/{sessionId}`
- `GET /api/diagnostics/{sessionId}/context?turnId=<optional>`
- `GET /api/diagnostics/{sessionId}/budget?turnId=<optional>`
- `GET /api/diagnostics/{sessionId}/history?turnId=<optional>`

## Behavior

- Uses persisted snapshots; it does not recompute diagnostics from mutable state.
- Returns `404` for context/budget/history when requested snapshot is not found.
- Returns `401` when API keys are configured and missing/invalid.

## Related Pages

- [Gateway API](gateway-api.md)
- [Feature: context diagnostics](../features/context-diagnostics-api.md)
- [Configuration](../configuration/configuration-reference.md)

## Source References

- `src/LeanKernel.Gateway/Endpoints.cs`
- `src/LeanKernel.Abstractions/Models/ContextDiagnosticsResponse.cs`
- `src/LeanKernel.Diagnostics/ContextDiagnosticsService.cs`
