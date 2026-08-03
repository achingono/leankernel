# Phase 25 Risk Register

| Risk | Impact | Likelihood | Mitigation |
|---|---|---|---|
| Runaway subprocess hangs the gateway process | Medium | Low | Linked `CancellationTokenSource` with hard timeout; `Process.Kill(entireProcessTree: true)` on cancellation |
| CLI binary makes unauthorized outbound network calls | Medium | Medium | Document `egress.allowHosts` as advisory for CLI tools; network policy remains the container boundary; emit startup advisory warning |
| Secret token leaks through `/proc` or process listing | High | Low | Inject bearer tokens as child-process env vars only — never pass as CLI arguments |
| PATH does not include `/app/data/skills/bin` | High | Low | Verify `docker-stack.yml` sets `PATH` with `skills/bin`; add startup binary-existence check |
| CLI binary not prebuilt / missing from NFS volume | High | Low | Startup logs warning for missing binary and skips the skill (does not crash) |
| Two CLI skills reference the same binary name | Low | Low | Already handled by existing `IToolRegistry.TryRegister` duplicate-name rejection |
| CLI tool output exceeds memory or log limits | Low | Medium | Truncate output at configurable max length (same pattern as HTTP: 12KB default) |
| Process.Start throws on missing binary | Medium | Low | Catch in handler, return `ToolResult` with error message |
| CLI skills incorrectly skipped by HTTP validation in LoadSkillFile | High | Medium | Branch by runtime type BEFORE HTTP validation (BaseUrl, host extraction, bearer secretRef) in LoadSkillFile |
| Egress advisory semantics unclear to operators | Medium | Medium | Emit explicit startup warning when CLI skill declares egress.allowHosts; document in tool-runtime.md and tool-configuration.md |
| Binary resolution contract ambiguous (warn vs crash) | Medium | Low | Specify: standard PATH lookup; missing binary logs warning and skips skill; no crash |
