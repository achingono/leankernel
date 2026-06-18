# Appsettings Reference

Gateway runtime defaults are defined in appsettings files.

## Files

- `src/LeanKernel.Gateway/appsettings.json`: baseline defaults
- `src/LeanKernel.Gateway/appsettings.Development.json`: local development overrides

## Important Defaults

- API key auth is optional by default (`LeanKernel:Gateway:ApiKey` empty).
- Diagnostics and context diagnostics are enabled by default.
- Hardening rate limit is enabled by default.
- Spend guard is disabled by default.

## Related Pages

- [Configuration reference](configuration-reference.md)
- [Environment variables](environment-variables.md)
- [Operations](../operations/index.md)
