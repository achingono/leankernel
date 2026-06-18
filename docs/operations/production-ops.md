# Production Operations

LeanKernel production hardening combines rate limiting, provider health tracking, spend controls, and graceful degradation.

## Core Components

- `RateLimitingMiddleware`: per-caller minute/hour/concurrency controls.
- `ProviderHealthTracker`: health probe state for dependencies.
- `SpendGuardService`: configurable spend warnings and blocks.
- `GracefulDegradationPolicy`: non-throwing fallback behavior.
- `CorrelationIdMiddleware`: propagates `X-Correlation-Id` for traceability.

## Default Behavior

- Health endpoints remain available under load.
- Degradation policy prefers bounded responses over hard failure.
- Spend guard is disabled by default and can be enabled per deployment.

## Related Pages

- [Health and observability](health-and-observability.md)
- [Configuration: appsettings](../configuration/appsettings-reference.md)
- [API health endpoint](../api/gateway-api.md)

## Source References

- `src/LeanKernel.Gateway/LeanKernelHardeningServiceCollectionExtensions.cs`
- `src/LeanKernel.Gateway/Middleware/RateLimitingMiddleware.cs`
- `src/LeanKernel.Diagnostics/SpendGuard/SpendGuardService.cs`
