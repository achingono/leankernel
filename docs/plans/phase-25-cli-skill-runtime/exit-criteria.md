# Phase 25 Exit Criteria

## Gate Checklist
- [ ] `RawRuntime.Command`, `RawInvoke.Argv`, `RawInvoke.Flags` are parsed from YAML frontmatter.
- [ ] `SkillParser` accepts `runtime.type: cli` (alongside existing `http`).
- [ ] Unknown runtime types are still rejected with a logged reason.
- [ ] `SkillParser` validates CLI-specific rules: requires `runtime.command` for `cli` type; validates `invoke.argv` is a list; validates `invoke.flags` is a dict; validates boolean flag parameters declare `type: boolean`.
- [ ] CLI skills produce distinct startup log lines (`"CLI tool '{Name}' registered from {Path}."`).
- [ ] Missing binaries on PATH produce a logged warning and skip the skill (don't crash startup).
- [ ] `LoadSkillFile()` branches by `skill.Runtime.Type` before HTTP validation — CLI skills do not require `BaseUrl` and skip host extraction/bearer secretRef validation.
- [ ] CLI skills with `egress.allowHosts` log advisory warning at startup: `"CLI tool '{Name}' declares egress.allowHosts; enforcement is advisory (gateway cannot intercept subprocess network calls)."`
- [ ] `DynamicCliTool.Create()` builds `ToolDefinition` with correct `{name}_{operation.id}` naming.
- [ ] Positional `Argv` items are passed in order before flags.
- [ ] Named `Flags` are appended as `flagName + value` for non-boolean params.
- [ ] Boolean flags are passed as `--flag` when true, omitted when false.
- [ ] Bearer auth secrets are injected as child-process environment variables (never CLI args).
- [ ] Timeout enforcement: subprocess is killed when `TimeoutSeconds` is exceeded (process tree kill).
- [ ] Stdout is returned as tool output on exit code 0.
- [ ] Stderr is returned as tool error on non-zero exit code.
- [ ] Output truncated at configurable max length (default 12KB).
- [ ] All 4 existing CLI skills in the swarm repo (`blog`, `image`, `ms-todo-cli`, `simplefin-cli`) load without errors.
- [ ] The emanate HTTP skill still loads correctly alongside CLI skills (regression).
- [ ] `dotnet build` passes.
- [ ] Targeted `dotnet test` coverage passes for CLI argument construction, boolean handling, PATH resolution, timeout, stdout/stderr capture, secret injection, secret resolution failure, missing binary, non-zero exit.

## Approval Table

| Role | Name | Status | Notes |
|---|---|---|---|
| Owner | OpenCode | Pending | |
| Reviewer | | Pending | |
| Approver | Repository owner | Pending | |
