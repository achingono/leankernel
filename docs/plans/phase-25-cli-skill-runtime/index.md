# Phase 25 CLI Skill Runtime

## Companion Documents
- [Inputs](inputs.md)
- [Activities](activities.md)
- [Outputs](outputs.md)
- [Exit Criteria](exit-criteria.md)
- [Risk Register](risk-register.md)
- [Evidence](evidence.md)

## Objective
Extend the Phase 01 dynamic skill runtime to support `runtime.type: cli` skills. This enables the gateway to load user-defined CLI tools from `SKILL.md` manifests, execute them as subprocesses, capture stdout/stderr, and expose them as first-class agent-visible tools — the same pattern that existed in the older LeanKernel codebase (`~/source/repos/leankernel/src/LeanKernel.Plugins/BuiltIn/Skills/`) but was deferred from Phase 01.

## Scope
This phase adds CLI execution capability to the existing dynamic skill loader. It reuses the Phase 01 registry, governance policy, and framework (`IToolRegistry`, `ToolGovernancePolicy`, `ToolDefinition`, `SkillParser`). It does not change the HTTP runtime, turn pipeline, model routing, or UI.

## In Scope
- Add `RawRuntime.Command` property for the binary name.
- Add `RawInvoke.Argv` (positional args list) and `RawInvoke.Flags` (named flag mappings) properties.
- Extend `SkillParser.MapToDefinition()` to accept `runtime.type: cli` and map CLI-specific fields; reject unknown runtime types with a logged reason.
- Extend `SkillRuntimeConfig` with `Command` property for parsed CLI skill runtime configuration.
- Modify `LoadSkillFile()` in `IServiceProviderExtensions.cs` to branch by `skill.Runtime.Type` before HTTP-specific validation (BaseUrl, host extraction), so CLI skills bypass HTTP validation.
- Create a `DynamicCliTool.Create()` that builds `ToolDefinition` instances with `Process.Start`-based handlers using `ArgumentList` (no shell).
- Implement CLI argument construction: positional `argv` items first, then `flags` derived from LLM-supplied parameters; boolean flags present when true, omitted when false.
- Implement secret resolution for CLI bearer auth (same pattern as HTTP: `/run/secrets/<ref>` or `SKILL__<REF>`), injected as child-process environment variables, never CLI arguments.
- Implement stdout/stderr capture with timeout enforcement via linked `CancellationTokenSource`; kill process tree on timeout.
- Add governance: egress allowlisting is **advisory only** for CLI tools (gateway cannot intercept subprocess network calls); emit startup warning when CLI skill declares `egress.allowHosts`.
- Update `SkillParser` logging to emit distinct info/warning messages for CLI skills (registered, missing binary, rejected runtime type) instead of generic "invalid" message.
- Update the `SKILL.md` schema documentation in `docs/operations/tool-configuration.md`, `docs/features/tool-runtime.md`, and the Phase 01 Appendix A.
- Add unit and integration tests covering CLI argument construction, boolean flag handling, PATH resolution, timeout enforcement, stdout/stderr capture, secret injection, missing binary, non-zero exit codes.
- Verify the container `PATH` includes `/app/data/skills/bin` (already configured in `docker-stack.yml`); binary resolution uses standard PATH lookup with warning+skip if missing.

## Out of Scope
- Opaque binary verification (e.g., integrity hashing or digital signatures).
- Sandboxing or container-level process isolation beyond the existing container boundary.
- Interactive CLI tools that require stdin beyond the initial input.
- Long-running daemon processes or background services started via CLI skills.
- CLI tools that require terminal emulation (PTY).

## Entry Criteria
- Phase 01 tool runtime (`IToolRegistry`, `ToolGovernancePolicy`, `SkillParser`, `ToolDefinition`) is operational.
- Prebuilt standalone CLI binaries exist in the external swarm deployment repo under `~/source/repos/swarm/deploy/leankernel/skills/bin/` and are accessible on the container `PATH`.
- Source reference captured as behavioral target: `~/source/repos/leankernel/src/LeanKernel.Plugins/BuiltIn/Skills/DynamicSkillTool.cs` (old codebase).
- Existing `runtime.type: cli` SKILL.md files in the external swarm repo (`~/source/repos/swarm/deploy/leankernel/skills/{blog,image,ms-todo-cli,simplefin-cli}/`) serve as the functional specification.

## Exit Criteria
The gateway can load and execute CLI-type dynamic skills, passing parameters as positional argv items and named flags, resolving bearer secrets, enforcing timeouts, capturing stdout as tool output, and respecting egress allowlists for network-bound CLI binaries. CLI skills produce distinct startup logging rather than the generic "invalid" warning.

## Roles
- Owner: OpenCode
- Reviewer: separate agent session / model review
- Approver: repository owner
