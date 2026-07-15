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
| Channel entity (policy home) | `src/Common/LeanKernel.Core/Entities/ChannelEntity.cs` | Where per-channel sharing policy is persisted |
| Memory client (policy consumer) | `src/Services/LeanKernel.Gateway/Memory/GBrainMemoryClient.cs` | Phase 10 enforces the policy here (`memory/{tenantId}/{...}/{channelId}/{key}`) |
