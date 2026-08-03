# Phase 25 Outputs

## Mandatory Outputs

| Output | Description | Format |
|---|---|---|
| CLI DTO extensions | `RawRuntime.Command`, `RawInvoke.Argv`, `RawInvoke.Flags` properties added to existing raw DTOs | C# code |
| Parser extension | `SkillParser.MapToDefinition()` accepts `runtime.type: cli`, maps CLI fields, validates CLI-specific rules (required command, argv/flags format, boolean flag params), logs distinct diagnostics | C# code |
| CLI domain model | `SkillRuntimeConfig.Command` property for parsed binary name | C# code |
| DynamicCliTool factory | `DynamicCliTool.Create()` producing `ToolDefinition` with `Process.Start`-based handler using `ArgumentList` (no shell) | C# code |
| Arg construction | Positional `argv` + named `flags` argument builder, boolean-flag semantics (present when true, omitted when false) | C# code |
| Secret injection | Bearer token injected as child-process env var (`SKILL__TOKEN`), never CLI argument | C# code |
| Timeout enforcement | Linked `CancellationTokenSource` kills process tree (`Process.Kill(entireProcessTree: true)`) on timeout | C# code |
| Execution dispatch | `LoadSkillFile()` branches by `skill.Runtime.Type` before HTTP validation; routes to `DynamicCliTool.Create()` or `DynamicSkillTool.Create()` | C# code |
| Binary resolution | Standard PATH lookup; missing binary logs warning and skips skill (no crash) | C# code |
| Diagnostics | Distinct log messages: CLI registered, missing binary warning, rejected runtime type, advisory egress warning | C# code |
| Schema documentation | CLI extension to Phase 01 Appendix A, `docs/operations/tool-configuration.md`, AND `docs/features/tool-runtime.md` | Markdown |
| Unit tests | Arg construction, boolean flags, PATH resolution (found/not found), timeout, stdout/stderr capture, secret injection, secret resolution failure | C# code |
| Integration tests | Real SKILL.md loading with `runtime.type: cli`, binary execution, missing binary warning, egress advisory warning | C# code |

## Optional Outputs
- E2E verification that the blog-cli review-gate workflow executes through a CLI skill tool tip-to-tail.

## Output Quality Checklist
- [ ] All existing HTTP skills still parse and load correctly (no regression)
- [ ] CLI skills produce distinct "registered" log lines (not the generic "invalid" warning)
- [ ] Missing binaries produce actionable warnings at startup and skill is skipped
- [ ] Secret tokens never appear in CLI argument listing (process env only)
- [ ] Runaway subprocesses are killed on timeout (process tree)
- [ ] CLI skills bypass HTTP validation in LoadSkillFile (no BaseUrl requirement)
- [ ] CLI skills with egress.allowHosts log advisory warning at startup
- [ ] Boolean flag parameters work correctly (flag when true, omitted when false)
- [ ] Output truncation at configurable max length (default 12KB)
