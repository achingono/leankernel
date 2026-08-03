# Phase 25 Handoff Document

## Implementation Progress Summary

### Completed Work
1. **Raw DTO Extensions** (`src/Common/LeanKernel.Logic/Tools/Dynamic/`):
   - `RawRuntime`: Added `Command` property for binary resolution.
   - `RawInvoke`: Added `Argv` (positional args) and `Flags` (named flag mapping, nullable values) properties.
   - `RawOperation`: Changed `Parameters` to `object?` to support both flat and JSON-schema style parameter formats.
   - Removed obsolete `RawParameter.cs`.

2. **Domain Models** (`src/Common/LeanKernel.Logic/Tools/Dynamic/`):
   - Extended `SkillRuntimeConfig` with `Command` property (flat config model for HTTP and CLI skill configurations).
   - Extended `SkillOperation` with `Argv` (`IReadOnlyList<string>`) and `Flags` (`IReadOnlyDictionary<string, string?>`).

3. **Dynamic Skill Parsing** (`SkillParser.cs`):
   - Added `ILogger?` support for diagnostics logging.
   - Extended `MapToDefinition` to accept `runtime.type: cli` alongside `http`.
   - Added CLI operation validation (verifying flag keys map to declared parameters).
   - Added dual parameter converter (`ConvertParameters`) supporting both Phase 01 flat format and JSON-schema style format (`properties`/`required`).

4. **CLI Process Execution** (`DynamicCliTool.cs`, `SkillSecretResolver.cs`, `DynamicCliSettings.cs`):
   - Created `DynamicCliTool.Create()` returning `ToolDefinition` wrapping a process handler.
   - Extracted shared bearer secret resolution (`SkillSecretResolver`) resolving from `/run/secrets/<ref>` or `SKILL__<REF>` env var, injected into child processes as environment variables (`SKILL__<REF>`).
   - Implemented argument construction: positional `Argv` items first, followed by parameters mapped to flags.
   - Implemented boolean flag handling (`type: boolean` parameter present when true, omitted when false) and null flag mapping (passed as bare positional arguments).
   - Implemented process execution with stdout/stderr capture, process-tree killing on timeout via `WaitAsync(timeoutCts.Token)`, and output truncation controlled by `Agents:Tools:DynamicCli:MaxOutputChars` (default 12,000 chars).

5. **Startup & Registration Dispatch** (`IServiceProviderExtensions.cs`, `BinaryPathResolver.cs`):
   - Created `BinaryPathResolver` for standard `PATH` environment variable binary resolution.
   - Modified `LoadSkillFile` to branch by `skill.Runtime.Type` before HTTP validation, allowing CLI skills to bypass `BaseUrl` and host-extraction rules.
   - Emits distinct startup log messages for CLI skills (`CLI tool '{Name}' registered from {Path}.`, missing binary on PATH warnings, and advisory egress warnings when CLI skills declare `egress.allowHosts`).

6. **Unit Tests** (47/47 passing):
   - Updated `SkillParserTests` for CLI type, schema parameters, null flags, missing command, and unknown type rejection.
   - Added `DynamicCliToolTests` covering argument ordering, boolean flags, null flags, stdout/stderr capture, process-tree timeout kill, secret injection (verifying secrets never appear on command line), missing binary, and output truncation.
   - Added `BinaryPathResolverTests` for PATH resolution logic.

---

## Outstanding Tasks

1. **Integration Tests**:
   - Create integration tests in `test/LeanKernel.Tests.Integration/` (e.g. `DynamicCliSkillIntegrationTests.cs`) covering:
     - End-to-end skill loading and tool registration via `IToolRegistry`.
     - Execution of a test CLI tool.
     - Warning & skip behavior when binary is missing from `PATH`.
     - Advisory warning when CLI skill declares `egress.allowHosts`.

2. **Documentation Updates**:
   - Update `docs/features/tool-runtime.md` with CLI skill manifest schema and runtime behavior.
   - Update `docs/operations/tool-configuration.md` with `Agents:Tools:DynamicCli` settings and PATH configuration details.
   - Update Phase 01 Appendix A schema documentation in `docs/plans/phase-01-built-in-tools/activities.md` to document Phase 25 CLI runtime extensions.

3. **Verification & Quality Checks**:
   - Run full unit and integration test suite (`dotnet test`).
   - Run `scripts/quality/sonarqube-scan.sh` and address any reported issues.
   - Perform sub-agent deep review per `AGENTS.md` guidelines.
