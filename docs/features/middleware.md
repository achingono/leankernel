# Gateway Middleware

The Gateway host applies request middleware for correlation and API rate limiting.

## Middleware Pipeline

- `CorrelationIdMiddleware` runs first to ensure `X-Correlation-Id` is present on request/response and to attach correlation context to logs/metrics.
- `RateLimitingMiddleware` enforces hardening limits for `/api/*` routes using:
  - per-partition requests per minute
  - per-partition requests per hour
  - per-partition concurrent requests

## Partitioning and Exclusions

- Rate-limit partition key is `X-Api-Key` when provided; otherwise remote IP; otherwise `anonymous`.
- `/api/health` is excluded from rate limiting.
- Non-API routes (including Blazor UI and `/healthz`) are not rate-limited by this middleware.

## Failure Behavior

- Rejected requests return `429 Too Many Requests` with a JSON reason payload.
- Correlation and rate-limit events are recorded through `LeanKernelMetrics`.

## Related Pages

- [Gateway API](../api/gateway-api.md)
- [Configuration reference](../configuration/configuration-reference.md)

## Source References

- `src/LeanKernel.Gateway/Program.cs`
- `src/LeanKernel.Gateway/Middleware/CorrelationIdMiddleware.cs`
- `src/LeanKernel.Gateway/Middleware/RateLimitingMiddleware.cs`
