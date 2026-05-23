# Phase 2 Channel Expansion PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and implementers
- **Phase goal:** Implement the Phase 2 channel expansion slice so non-HTTP channels normalize inbound traffic through the shared `IAgentRuntime` entry point, with per-channel auth and a Signal daemon adapter.
- **Plan review:** Reviewed by `gpt-5.4-mini`. Review outcome: proceed after clarifying the exact Signal HTTP contract (`GET /v1/receive/{account}?timeout=` and `POST /v2/send`), keeping auth fail-closed by default, making channel hosted-service subscription lifecycle idempotent, and updating the directly related docs/configuration references.

## Problem statement

Phase 1 delivered the HTTP gateway entry point, but Phase 2 still lacks a reusable channel abstraction for non-API message sources. The runtime therefore cannot yet accept Signal messages, enforce per-channel sender authorization, or reuse the same turn-processing path across inbound transports.

## Scope

This task will:

1. Add channel configuration objects to `LeanKernel.Abstractions.Configuration` and bind them through `LeanKernelConfig`.
2. Add `IChannel` and `IChannelRouter` contracts plus the shared inbound channel message model required for channel adapters.
3. Create a new `LeanKernel.Channels` project for channel auth, routing, Signal polling, and hosted-service lifecycle management.
4. Register the new project in `src/LeanKernel.sln`, reference it from `LeanKernel.Gateway`, and expose DI registration through `AddLeanKernelChannels`.
5. Implement fail-closed per-channel sender authorization using the requested `AllowedSenders` + `RequireAuth` configuration model.
6. Implement `SignalChannel` against the existing Signal HTTP bridge contract already documented under `config/signal/daemon.py`.
7. Update gateway configuration and local Docker Compose to include the disabled-by-default Signal channel slice.
8. Add unit tests for authentication, routing, Signal parsing/send behavior, and hosted-service lifecycle wiring.
9. Update the directly related contributor-facing docs for the new channel/configuration surface.
10. Attempt restore/build/test/coverage/Sonar validation and record the local blocker because `dotnet` is unavailable in this environment.

## Out of scope

- Extending `IAgentRuntime` beyond its current `RunTurnAsync` entry point.
- Adding channel-specific business logic outside the shared runtime path.
- Introducing websockets, streaming, or outbound durable queue semantics in this slice.
- Expanding channel auth beyond the requested allowlist-based sender enforcement model.
- Enabling Signal by default or handling Signal registration/verification automatically.

## Files to add

- `src/LeanKernel.Abstractions/Configuration/ChannelsConfig.cs`
- `src/LeanKernel.Abstractions/Interfaces/IChannel.cs`
- `src/LeanKernel.Abstractions/Interfaces/IChannelRouter.cs`
- `src/LeanKernel.Channels/LeanKernel.Channels.csproj`
- `src/LeanKernel.Channels/ChannelAuthenticator.cs`
- `src/LeanKernel.Channels/ChannelRouter.cs`
- `src/LeanKernel.Channels/SignalChannel.cs`
- `src/LeanKernel.Channels/ChannelHostedService.cs`
- `src/LeanKernel.Channels/ServiceCollectionExtensions.cs`
- `src/LeanKernel.Tests.Unit/Channels/ChannelAuthenticatorTests.cs`
- `src/LeanKernel.Tests.Unit/Channels/ChannelRouterTests.cs`
- `src/LeanKernel.Tests.Unit/Channels/SignalChannelTests.cs`
- `src/LeanKernel.Tests.Unit/Channels/ChannelHostedServiceTests.cs`
- `docs/features/channel-routing.md`
- `docs/configuration/phase-2-config.md`

## Files to update

- `src/LeanKernel.Abstractions/Configuration/LeanKernelConfig.cs`
- `src/LeanKernel.Gateway/LeanKernel.Gateway.csproj`
- `src/LeanKernel.Gateway/Program.cs`
- `src/LeanKernel.Gateway/appsettings.json`
- `src/LeanKernel.Gateway/appsettings.Development.json`
- `src/LeanKernel.Tests.Unit/LeanKernel.Tests.Unit.csproj`
- `src/LeanKernel.sln`
- `docker-compose.yml`
- `README.md`
- `docs/features/index.md`
- `docs/configuration/index.md`
- `docs/architecture/index.md`
- `docs/architecture/solution-structure.md`
- `docs/channel-consolidation.md`
- `docs/plans/index.md`

## Design notes

### Shared runtime path

- Inbound API traffic already reaches `IAgentRuntime.RunTurnAsync`; inbound channel traffic must do the same.
- `ChannelRouter` will map `ChannelMessage` into `LeanKernelMessage` and let the existing runtime/session pipeline decide whether to reuse or create a session.
- Channel replies are always sent through the originating adapter by matching `ChannelId`.

### Auth model

- The requested configuration model is intentionally simple for this slice:
  - `RequireAuth = false` allows the channel message.
  - `RequireAuth = true` requires `SenderId` membership in `AllowedSenders`.
  - Missing channel auth config fails closed so auth cannot be bypassed accidentally.
- Authorization outcomes must be logged with channel id, sender id, and denial reason.

### Signal adapter contract

- Use the existing HTTP bridge shape from `config/signal/daemon.py`:
  - Poll: `GET /v1/receive/{phoneNumber}?timeout={PollIntervalSeconds}`
  - Send: `POST /v2/send` with `{ recipients: [recipientId], message: "..." }`
- Parse normalized receive payloads shaped like `{ envelope: { ... }, account: "..." }`.
- Accept only inbound envelopes that contain a sender and text message body; ignore unsupported envelopes safely.
- Extract basic attachment metadata when the daemon payload includes attachment identifiers/content types; leave binary download/extraction out of scope.
- Apply bounded exponential backoff derived from `ReconnectDelaySeconds` and `MaxReconnectAttempts`.

### Hosted-service lifecycle

- `ChannelHostedService` should no-op when channels are globally disabled or no adapters are registered.
- Subscription/unsubscription must be idempotent so repeated start/stop paths do not double-register handlers.
- Per-message routing exceptions are logged and swallowed at the hosted-service boundary so the channel process keeps running.

## Test strategy

1. `ChannelAuthenticatorTests`
   - allows senders when auth is disabled
   - allows configured senders when auth is required
   - denies unknown senders when auth is required
   - denies channels without config
2. `ChannelRouterTests`
   - authorized inbound message invokes `IAgentRuntime.RunTurnAsync`
   - authorized inbound message sends the runtime response through the matching channel
   - unauthorized inbound message never invokes the runtime or channel send
   - unknown channel ids are rejected cleanly
3. `SignalChannelTests`
   - parses normalized receive payloads into `ChannelMessage`
   - ignores envelopes without text content or sender id
   - posts outbound message bodies to `/v2/send`
   - updates connection state across transient polling failures
4. `ChannelHostedServiceTests`
   - starts/stops registered channels
   - subscribes and routes inbound messages
   - does not crash when router throws

## Validation plan

1. Attempt `dotnet restore src/LeanKernel.sln`.
2. Attempt `dotnet build src/LeanKernel.sln --no-restore -v minimal`.
3. Attempt `dotnet test src/LeanKernel.sln --no-build -v minimal`.
4. Attempt `scripts/quality/test-coverage.sh`.
5. Attempt `scripts/quality/sonarqube-scan.sh`.
6. Record the blocker if `dotnet` remains unavailable.
7. Use source inspection and diff verification as fallback validation when the toolchain cannot run.

## Acceptance criteria

- `LeanKernelConfig` binds the new `Channels` section without disturbing existing config consumers.
- A new `LeanKernel.Channels` project exists and is wired into the solution, gateway, and unit-test project.
- Channel auth is enforced before runtime execution and defaults to denial when auth config is missing.
- `SignalChannel` polls the configured daemon over HTTP, emits inbound channel messages, and sends outbound responses through `/v2/send`.
- `ChannelHostedService` manages start/stop and routes inbound channel messages through `IChannelRouter`.
- Channel replies flow through the same `IAgentRuntime.RunTurnAsync` entry point used by the API gateway.
- Appsettings, Docker Compose, README, and related docs reflect the new disabled-by-default Signal channel support.
- Validation evidence records the local `dotnet` blocker if it persists.
