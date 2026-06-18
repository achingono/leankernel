# Health and Observability

LeanKernel exposes application and provider health status through both API and ASP.NET health checks.

## Health Surfaces

- `GET /api/health`: gateway/runtime/provider summary payload.
- `/healthz`: ASP.NET health checks endpoint.

## Provider Health

Provider state is tracked and returned with status, failure counters, and timestamps.

Tracked providers include:

- database
- LiteLLM
- GBrain
- Webwright (when configured)

## Telemetry

- OpenTelemetry configuration is controlled under top-level `OpenTelemetry:*`.
- OTLP exporter is used when `OpenTelemetry:Otlp:Endpoint` is configured.

## Related Pages

- [Production operations](production-ops.md)
- [Gateway API](../api/gateway-api.md)
- [Configuration reference](../configuration/configuration-reference.md)

## Source References

- `src/LeanKernel.Gateway/Endpoints.cs`
- `src/LeanKernel.Gateway/LeanKernelHardeningServiceCollectionExtensions.cs`
- `src/LeanKernel.Diagnostics/Health/ProviderHealthCheck.cs`
