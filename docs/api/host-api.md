# Host API Status

`LeanKernel.Gateway` is the active web host and API surface in the current solution.

## Current State

- `src/LeanKernel.Host` exists as a scaffold/legacy folder layout.
- `src/LeanKernel.Host` is not an active project in `src/LeanKernel.sln`.
- No active Host controllers/endpoints are currently shipped from that folder.

## Use This Instead

- Use [Gateway API](gateway-api.md) for current HTTP endpoints.
- Use [Diagnostics API](diagnostics-api.md) for diagnostics-specific endpoint details.

## Source References

- `src/LeanKernel.sln`
- `src/LeanKernel.Gateway/Program.cs`
