# Appsettings Reference

Gateway runtime defaults are defined in appsettings files.

## Files

- `src/LeanKernel.Gateway/appsettings.json`: baseline defaults
- `src/LeanKernel.Gateway/appsettings.Development.json`: local development overrides

## Important Defaults

- Gateway API auth is fail-closed by default:
  - `LeanKernel:Gateway:RequireApiKey=true`
  - `LeanKernel:Gateway:AllowAnonymous=false`
  - `LeanKernel:Gateway:ApiKey` / `LeanKernel:Gateway:ApiKeys` provide accepted keys
- Development overrides set `LeanKernel:Gateway:AllowAnonymous=true` for local DX.
- Diagnostics and context diagnostics are enabled by default.
- Channel typing keepalive and continuation are enabled by default.
- Hardening rate limit is enabled by default.
- Spend guard is disabled by default.

## Related Pages

- [Configuration reference](configuration-reference.md)
- [Environment variables](environment-variables.md)
- [Operations](../operations/index.md)
