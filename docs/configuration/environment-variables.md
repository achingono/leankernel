# Environment Variables

Most runtime settings map from appsettings keys and can be overridden with environment variables.

## Common Overrides

- `LeanKernel__LiteLlm__BaseUrl`
- `LeanKernel__LiteLlm__ApiKey`
- `LeanKernel__GBrain__BaseUrl`
- `LeanKernel__Database__ConnectionString`
- `LeanKernel__Gateway__ApiKey`
- `LeanKernel__Gateway__ApiKeys__0` (and additional indexed keys)
- `LeanKernel__ForwardedAuth__Enabled`
- `LeanKernel__ForwardedAuth__RequireAuthenticatedUser`
- `LeanKernel__ForwardedAuth__RequireUserHeader`
- `LeanKernel__ForwardedAuth__UserHeader`
- `LeanKernel__ForwardedAuth__FallbackUserHeader`
- `LeanKernel__Skills__BasePaths__0` (and additional indexed paths)
- `LeanKernel__Skills__Enabled`
- `OpenTelemetry__Otlp__Endpoint`

## Notes

- Use double underscore (`__`) to represent nested JSON keys.
- Array values use zero-based indexes.
- Environment variables override appsettings values at runtime.
- `LeanKernel__Gateway__ApiKey` and `LeanKernel__Gateway__ApiKeys__*` are both honored; either can enable API key protection.
- `LeanKernel__Skills__Enabled` exists in configuration models; current runtime skill loading always runs and uses `BasePaths`.

## Related Pages

- [Configuration reference](configuration-reference.md)
- [Appsettings reference](appsettings-reference.md)
- [Gateway API auth behavior](../api/gateway-api.md)
