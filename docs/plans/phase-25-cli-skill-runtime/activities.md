# Phase 25 Activities

## Step-By-Step Activities
1. **Add CLI fields to raw YAML DTOs.**
   Extend `RawRuntime` with `Command` (string, the binary name). Extend `RawInvoke` with `Argv` (list of positional CLI arguments) and `Flags` (dictionary mapping parameter names to CLI flag strings like `"--title"`). These fields are already used by existing `runtime.type: cli` SKILL.md files in the swarm repo.

2. **Extend `SkillParser.MapToDefinition()` to accept `runtime.type: cli` with CLI-specific validation.**
   Remove the current hard rejection at `SkillParser.cs:78-83`. When `runtime.type` is `"cli"`:
   - Require `runtime.command` (non-empty) — log error and return null if missing
   - Map `runtime.command` to `SkillRuntimeConfig.Command`
   - Map `invoke.argv` (list of strings) and `invoke.flags` (dict<string,string>) to operation metadata
   - Skip HTTP-only validation (baseUrl, httpPath, httpMethod)
   - Validate boolean-flag parameters have `type: boolean` in parameters schema
   Return null only when `runtime.type` is an unknown/unrecognized value; log the rejected type.

3. **Extend domain model: `SkillRuntimeConfig`.**
   Extend `SkillRuntimeConfig` with:
   - `Command` (string) — the binary name resolved via PATH for CLI skills
   Keep runtime configuration flat on `SkillRuntimeConfig` (`Type`, `BaseUrl`, `Command`, `TimeoutSeconds`, `Auth`) with runtime behavior driven by `Type` ("http" or "cli").

4. **Modify `LoadSkillFile()` to branch by runtime type before HTTP validation.**
   In `IServiceProviderExtensions.cs:LoadSkillFile()`, after parsing the skill:
   - Check `skill.Runtime.Type` (or `skill.Runtime.GetType()`)
   - If `"cli"`: skip BaseUrl validation (lines 281-285), host extraction (lines 287-296), and bearer secretRef validation (lines 300-305) — CLI handles auth differently
   - If `"cli"` and `skill.AllowedHosts.Any()`: log advisory warning `"CLI tool '{Name}' declares egress.allowHosts; enforcement is advisory (gateway cannot intercept subprocess network calls)."`
   - Dispatch to `DynamicCliTool.Create()` for CLI, `DynamicSkillTool.Create()` for HTTP
   - Binary existence check: resolve `skill.Runtime.Command` via PATH; if not found, log warning `"CLI tool '{Name}' command '{Command}' not found on PATH. Skipping."` and return (don't register, don't crash)

5. **Create `DynamicCliTool.Create()`.**
   Following the reference at `5033dafc:src/LeanKernel.Plugins/BuiltIn/Skills/DynamicSkillTool.cs`, create a static factory method that builds a `ToolDefinition` with a `Process.Start`-based handler:
   - Resolve the binary via standard PATH lookup (the binaries live at `/app/data/skills/bin/` which is on the container PATH).
   - Build argument list: positional `Argv` items first, then mapped `Flags` from LLM-supplied parameters.
   - For boolean parameters mapped to flags: pass only the flag name when true, omit when false.
   - Set `RedirectStandardOutput = true`, `RedirectStandardError = true`, `UseShellExecute = false`.
   - Enforce timeout from `skill.Runtime.TimeoutSeconds` using a linked `CancellationTokenSource`.
   - On timeout, kill the process tree (`Process.Kill(entireProcessTree: true)`) and return a timeout error.
   - Capture stdout/stderr asynchronously during execution.
   - Return stdout on success (exit code 0), stderr on failure (non-zero exit).
   - Truncate output at configurable max length (default 12KB, same as HTTP).

6. **Wire CLI execution into the runtime registration path.**
   In `LoadSkillFile()`, after the runtime-type branch (Activity 4), call `DynamicCliTool.Create(skill, op, scopeFactory)` for CLI skills. Both HTTP and CLI return `ToolDefinition` instances registered into the shared `IToolRegistry`.

7. **Improve startup validation and diagnostics.**
   - Log a distinct info message for CLI tools: `"CLI tool '{Name}' registered from {Path}."`
   - Log a distinct warning when the binary is not found on PATH: `"CLI tool '{Name}' command '{Command}' not found on PATH. Skipping."`
   - Keep the existing "invalid" warning for files with unrecognized runtime types.
   - Log the registered binary path at debug level.
   - Log advisory egress warning when CLI skill has `egress.allowHosts` (see Activity 4).

8. **Add secret resolution for CLI bearer auth.**
   Reuse the same `ResolveSecret` pattern from `DynamicSkillTool.cs` (lines 182-208): resolve from `/run/secrets/<ref>` or `SKILL__<REF>` env var. The resolved token should be set as an environment variable (`SKILL__TOKEN` or similar) in the child process rather than passed on the command line, to avoid leaking secrets through `/proc` or process listings.

9. **Update `SKILL.md` schema documentation in live docs.**
   Extend the schema in:
   - `docs/operations/tool-configuration.md`
   - `docs/features/tool-runtime.md`
   - Phase 01 Appendix A in `docs/plans/phase-01-built-in-tools/activities.md`
   With the CLI runtime extension:
   - `runtime.type: cli` (with `runtime.type: http` still the default)
   - `runtime.command: <binary-name>` (resolved via PATH)
   - `invoke.argv: [pos1, pos2]` (positional arguments)
   - `invoke.flags: { param_name: "--flag-name" }` (named parameter → CLI flag mapping)

10. **Add tests.**
    Follow the pattern from `5033dafc:test/LeanKernel.Tests.Unit/Plugins/DynamicSkillToolTests.cs`:
    - Unit test: CLI argument construction with argv + flags.
    - Unit test: Boolean flag handling (flag present when true, absent when false).
    - Unit test: PATH resolution for the binary (found / not found).
    - Unit test: Timeout enforcement kills the process tree.
    - Unit test: stdout capture on exit code 0.
    - Unit test: stderr capture on non-zero exit code.
    - Unit test: Secret env-var injection for bearer auth (not in CLI args).
    - Unit test: Secret resolution failure returns error.
    - Integration test: Load a real SKILL.md with `runtime.type: cli` and verify registration.
    - Integration test: Execute a known binary (e.g., `echo` or a test script) and verify output.
    - Integration test: Missing binary on PATH produces a logged warning and skill is skipped.
    - Integration test: CLI skill with `egress.allowHosts` logs advisory warning at startup.

11. **Run verification.**
    - Verify the swarm repo's 4 CLI skills (`blog`, `image`, `ms-todo-cli`, `simplefin-cli`) load and register at startup without warnings.
    - Verify the emanate HTTP skill still loads correctly (regression).
    - Verify `dotnet build` and targeted `dotnet test` pass.
    - Verify CLI tools appear in `registry.Tools` with correct names (`{name}_{operation.id}`).
    - Verify CLI tool execution produces expected stdout/stderr behavior.

## Review Focus
- The CLI execution path must not block the HTTP runtime or degrade startup time.
- Secret tokens must not leak through CLI arguments (use environment variables).
- Process timeout must be enforced reliably; runaway subprocesses must be killed.
- The PATH must include `/app/data/skills/bin` (already configured in `docker-stack.yml`).
- CLI tools with `egress.allowHosts` should be documented as advisory-only (the gateway cannot intercept the binary's network calls).
- `LoadSkillFile` must branch by runtime type BEFORE HTTP-specific validation to avoid incorrectly skipping CLI skills.
- Binary resolution uses standard PATH lookup; missing binary logs warning and skips skill (no crash).

## Appendix A: CLI SKILL.md schema extension (Phase 25)

A CLI-type SKILL.md reuses the same YAML frontmatter structure as HTTP, with additional fields:

```yaml
---
name: blog                    # required; skill identifier
description: Hugo blog tools  # required; surfaced in each tool's description
metadata:
  category: publishing        # optional; for governance allowlists
runtime:
  type: cli                   # required; distinguishes from "http"
  command: blog-cli           # required; binary resolved via container PATH
  timeoutSeconds: 120         # optional; default 30
  auth:
    type: none                # none | bearer; bearer resolved as env var, not CLI arg
    secretRef: my_token       # required when type=bearer; set as SKILL__TOKEN env var
  egress:
    allowHosts:               # advisory for CLI tools (gateway cannot intercept)
      - api.github.com        #   binary's outbound connections
operations:
  - id: create_draft          # required; unique within skill
    summary: Create a draft post  # required; surfaced to the model
    invoke:
      argv:                   # positional arguments passed before flags
        - create_draft
      flags:                  # named arguments mapped to CLI flags
        title: "--title"      #   key = parameter name, value = flag string
        body: "--body"        #   boolean params: flag present when true, absent when false
    parameters:
      title:
        type: string
        description: Post title
        required: true
      body:
        type: string
        description: Post body content
---
```

Loader rules:
- `runtime.command` is resolved via standard PATH lookup. The container's `PATH` includes `/app/data/skills/bin` (set in `docker-stack.yml`).
- `invoke.argv` items are passed as positional arguments before any flags.
- `invoke.flags` keys correspond to parameter names. The flag value is the CLI switch string (e.g., `"--title"`). Boolean parameters pass only the flag (no value) when true; the flag is omitted when false.
- Bearer auth secrets are injected as environment variables (`SKILL__<REF>`) in the child process, never passed on the command line.
- Egress allowlisting is advisory for CLI tools. The gateway cannot intercept subprocess outbound connections.
- A manifest using unsupported `runtime.type` values is rejected with a logged reason.
- Missing `runtime.command` for `runtime.type: cli` is a validation error (logged, skill skipped).
- Malformed `invoke.argv` (not a list) or `invoke.flags` (not a dict) is a validation error.
- Boolean parameters mapped to flags must declare `type: boolean` in parameters schema.
