# Environment Variables

Most runtime settings map from appsettings keys and can be overridden with environment variables.

## Common Overrides

- `LeanKernel__LiteLlm__BaseUrl`
- `LeanKernel__LiteLlm__ApiKey`
- `LeanKernel__GBrain__BaseUrl`
- `LeanKernel__Database__ConnectionString`
- `LeanKernel__Gateway__ApiKey`
- `LeanKernel__Gateway__ApiKeys__0` (and additional indexed keys)
- `OpenTelemetry__Otlp__Endpoint`

## Notes

- Use double underscore (`__`) to represent nested JSON keys.
- Array values use zero-based indexes.
- Environment variables override appsettings values at runtime.

## Related Pages

- [Configuration reference](configuration-reference.md)
- [Appsettings reference](appsettings-reference.md)
- [Gateway API auth behavior](../api/gateway-api.md)
