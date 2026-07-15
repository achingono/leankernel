# Phase 06 Outputs

## Mandatory Outputs

| Output | Description | Format |
| --- | --- | --- |
| Channel terminal abstraction | Adapter contract + `ChannelRouter` + Gateway HTTP client (claims submission + SSE consumption) | C# source |
| Signal terminal | `src/Terminals/LeanKernel.Channels.Signal` — poll/receive/send/reconnect against Signal daemon | C# project |
| Teams terminal | `src/Terminals/LeanKernel.Channels.Teams` — receive/send Microsoft Teams Bot Framework activities | C# project |
| Per-binding credential provisioning | Sender→(tenant, user) binding + revocable per-binding credential (short-lived tokens preferred) | C# source |
| Gateway channel-claim support | Trust/validate channel token issuer(s); resolve `TenantId` from binding and `ChannelId` from claims (not hardcoded `openai-http`); resolve-not-create existing user | C# source |
| Channel authenticator | Fail-closed by construction: only pre-provisioned senders accepted (terminal + Gateway) | C# source |
| Keep-alive | Typing-indicator keep-alive for long-running turns, driven by the Gateway streaming response | C# source |
| Attachment parsing | Inbound attachment directive parser (Signal + Teams) | C# source |
| Memory sharing policy | Directional per-channel `Share`/`Access` allow-lists (wildcard default) + persistence + resolution contract consumed by Phase 10 | C# + EF migration |
| Configuration + validation | Channel enable, Signal daemon, Teams Bot Framework, binding/credential provisioning, memory policy defaults | C# + appsettings |
| Tests | Channel-claim trust, tenant/user/channel resolution from claims, fail-closed rejection, Signal + Teams receive/send/reconnect, keep-alive over SSE, attachments, memory-policy resolution | xUnit projects |
| Documentation | Channels feature docs (terminals, Option B claims/binding model, sharing policy) | Markdown |

## Optional Outputs
- Channel activity metrics surface reserved for Phase 08 diagnostics.

## Output Quality Checklist
- [ ] All mandatory outputs produced
- [ ] All outputs reviewed before gate
- [ ] Evidence log updated with output references
