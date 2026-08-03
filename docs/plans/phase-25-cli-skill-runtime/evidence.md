# Phase 25 Evidence

## Evidence Log

| Item | Reference | Notes |
|---|---|---|
| Old CLI execution pattern | commit `5033dafc`: `src/LeanKernel.Plugins/BuiltIn/Skills/DynamicSkillTool.cs` | Reference `ExecuteCliAsync` — uses `ProcessStartInfo`, `ArgumentList`, `RedirectStandardOutput/Error`, `ReadStreamAsync`, timeout via `CancellationTokenSource`, boolean-flag detection |
| Old CLI schema parser | commit `5033dafc`: `src/LeanKernel.Plugins/BuiltIn/Skills/SkillParser.cs` | Reference `RawInvoke.Argv`, `RawInvoke.Flags`, `RawRuntime.Command` — these DTO fields need to be added to the current rebuild |
| Old CLI test coverage | commit `5033dafc`: `test/LeanKernel.Tests.Unit/Plugins/DynamicSkillToolTests.cs` | Reference test patterns for argument construction, boolean handling, secret resolution |
| Current HTTP-only parser | `src/Common/LeanKernel.Logic/Tools/Dynamic/SkillParser.cs:78-83` | Current `if (runtimeType != "http") return null` must be extended to accept `"cli"` with CLI-specific validation |
| Current HTTP-only DTOs | `src/Common/LeanKernel.Logic/Tools/Dynamic/RawInvoke.cs`, `RawRuntime.cs` | Missing `Command`, `Argv`, `Flags` properties |
| Current HTTP-only runtime model | `src/Common/LeanKernel.Logic/Tools/Dynamic/SkillRuntimeConfig.cs` | Extended with `Command` property for CLI runtime configuration |
| Current HTTP validation in LoadSkillFile | `src/Common/LeanKernel.Logic/Extensions/IServiceProviderExtensions.cs:279-305` | BaseUrl check (281-285), host extraction (287-296), bearer secretRef validation (300-305) — CLI skills must bypass |
| Existing CLI SKILL.md files | `~/source/repos/swarm/deploy/leankernel/skills/{blog,image,ms-todo-cli,simplefin-cli}/SKILL.md` | Functional spec for the CLI format; includes argv, flags, boolean params, bearer auth, egress allowHosts |
| Prebuilt CLI binaries | `~/source/repos/swarm/deploy/leankernel/skills/bin/{blog-cli,image-cli,ms-todo-cli,simplefin-cli,paddleocr}` | Published to NFS skills volume, on container PATH |
| Container PATH config | `~/source/repos/swarm/deploy/leankernel/docker-stack.yml` | `PATH` env var includes `/app/data/skills/bin` |
| Live docs for tool runtime | `docs/features/tool-runtime.md`, `docs/operations/tool-configuration.md` | Must be updated with CLI schema extension |
| Phase 01 Appendix A | `docs/plans/phase-01-built-in-tools/activities.md` | Historical reference; must be extended with CLI schema |
| Implementation diff | Pending | |
| Build and test results | Pending | |
