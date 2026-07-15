# Phase 06 Evidence

## Evidence Log

| Item | Reference | Notes |
| --- | --- | --- |
| Source channel abstraction | `~/source/repos/leankernel/src/LeanKernel.Channels/{ChannelHostedService,ChannelRouter}.cs` | Behavioral reference |
| Source channel auth | `~/source/repos/leankernel/src/LeanKernel.Channels/ChannelAuthenticator.cs` | Fail-closed reference |
| Source Signal adapter | `~/source/repos/leankernel/src/LeanKernel.Channels/SignalChannel.cs` | Behavioral reference |
| Source keep-alive | `~/source/repos/leankernel/src/LeanKernel.Channels/TypingIndicatorKeepAlive.cs` | Behavioral reference |
| Source attachment parsing | `~/source/repos/leankernel/src/LeanKernel.Channels/SignalAttachmentParser.cs` | Behavioral reference |
| Rebuild identity/channel | `src/Common/LeanKernel.Logic/Providers/IdentityResolver.cs` | Integration point |
| Tenant resolution (host-based) | `src/Services/LeanKernel.Gateway/Providers/TenantResolutionMiddleware.cs`, `IdentityResolver.ResolveTenantAsync` | Web resolves tenant by HTTP host; channels need a channel→tenant scope instead |
| User resolution (issuer/subject) | `IdentityResolver.ResolveOrCreateUserAsync`, `src/Common/LeanKernel.Core/Entities/UserEntity.cs` (Issuer/Subject) | Reused to map channel-native sender to a persisted user (resolve-not-create) |
| Gateway hardcoded channel | `src/Services/LeanKernel.Gateway/Providers/TenantResolutionMiddleware.cs` (`ChannelEntity.OpenAiHttpName`) | Must instead resolve `ChannelId` from channel claims |
| Signal terminal project | `src/Terminals/LeanKernel.Channels.Signal/{Program.cs,TerminalService.cs,Settings.cs}` | Implemented JSON-RPC socket transport, multi-account receive, DB-backed token lookup |
| Teams terminal project | `src/Terminals/LeanKernel.Channels.Teams/{Program.cs,TerminalService.cs,Settings.cs}` | Implemented Bot Framework webhook ingress, connector egress, DB-backed token lookup |
| Signal sidecar topology | `docker-compose.yml` (`signal-cli`, `signal-terminal`) | Uses source-repo daemon image (`bbernhard/signal-cli-rest-api`, `MODE=json-rpc`) with shared `signal-data` volume |
| Channel entity (policy home) | `src/Common/LeanKernel.Core/Entities/ChannelEntity.cs` | Where per-channel sharing policy is persisted |
| Sender binding token persistence | `src/Common/LeanKernel.Core/Entities/ChannelSenderBindingEntity.cs`, `src/Common/LeanKernel.Data/Migrations/20260715192909_AddChannelBindingBearerToken.cs` | Added persisted terminal bearer token for binding lookup |
| Memory client (policy consumer) | `src/Services/LeanKernel.Gateway/Memory/GBrainMemoryClient.cs` | Phase 10 enforces the policy here (`memory/{tenantId}/{...}/{channelId}/{key}`) |
