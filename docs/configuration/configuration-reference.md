# Configuration Reference

Canonical runtime settings are loaded from `LeanKernel` and `OpenTelemetry` sections in Gateway appsettings.

## Primary Sections

- `LeanKernel:LiteLlm`
- `LeanKernel:Context`
- `LeanKernel:History`
- `LeanKernel:Retrieval`
- `LeanKernel:Routing`
- `LeanKernel:Orchestration`
- `LeanKernel:GBrain`
- `LeanKernel:Webwright`
- `LeanKernel:Identity`
- `LeanKernel:Database`
- `LeanKernel:DatabaseQuery`
- `LeanKernel:Diagnostics`
- `LeanKernel:Channels`
- `LeanKernel:Continuation`
- `LeanKernel:Enhancement`
- `LeanKernel:Learning`
- `LeanKernel:FileSystem`
- `LeanKernel:Hardening`
- `LeanKernel:Scheduler`
- `LeanKernel:DocumentIngestion`
- `LeanKernel:Skills`
- `LeanKernel:Gateway`
- `LeanKernel:ForwardedAuth`
- `OpenTelemetry`

## Key Notes By Section

- `LeanKernel:Gateway`: API key auth uses `ApiKey` (single key) and/or `ApiKeys` (array).
- `LeanKernel:ForwardedAuth`: forwarded identity auth settings (`Enabled`, `RequireAuthenticatedUser`, `RequireUserHeader`, `UserHeader`, `FallbackUserHeader`).
- `LeanKernel:Skills`: dynamic skill scan settings (`BasePaths`) are consumed by runtime skill loading.
- `LeanKernel:DocumentIngestion`: controls queue limits and watch-folder ingestion behavior for document import.
- `LeanKernel:Channels:Typing`: typing keepalive refresh cadence and stop timeout for channel turns.
- `LeanKernel:Continuation`: automatic continuation limits, progress throttling, and task-completion detection settings.
- `OpenTelemetry`: supports `OpenTelemetry:ConsoleExporterEnabled` and `OpenTelemetry:Otlp:Endpoint`.

## Related Pages

- [Environment variables](environment-variables.md)
- [Appsettings reference](appsettings-reference.md)
- [API docs](../api/index.md)

## Source References

- `src/LeanKernel.Gateway/appsettings.json`
- `src/LeanKernel.Gateway/appsettings.Development.json`
- `src/LeanKernel.Abstractions/Configuration/LeanKernelConfig.cs`
- `src/LeanKernel.Gateway/Program.cs`
- `src/LeanKernel.Gateway/Auth/ForwardedAuthHandler.cs`
