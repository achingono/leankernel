# Phase 25 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Phase 01 tool runtime | `src/Common/LeanKernel.Logic/Tools/*`, `src/Common/LeanKernel.Logic/Tools/Dynamic/*` | OpenCode |
| Old CLI execution code (reference) | commit `5033dafc` in leankernel repo: `src/LeanKernel.Plugins/BuiltIn/Skills/DynamicSkillTool.cs` (CLI path via `Process.Start`, `ArgumentList`, `ReadStreamAsync`, timeout via `CancellationTokenSource`) | OpenCode |
| Old CLI schema (reference) | commit `5033dafc`: `src/LeanKernel.Plugins/BuiltIn/Skills/SkillParser.cs` (RawInvoke.Argv, RawInvoke.Flags, RawRuntime.Command) | OpenCode |
| Old CLI test coverage (reference) | commit `5033dafc`: `test/LeanKernel.Tests.Unit/Plugins/DynamicSkillToolTests.cs` | OpenCode |
| Existing CLI SKILL.md files | `~/source/repos/swarm/deploy/leankernel/skills/{blog,image,ms-todo-cli,simplefin-cli}/SKILL.md` in the swarm repo | OpenCode |
| Prebuilt CLI binaries | `~/source/repos/swarm/deploy/leankernel/skills/bin/{blog-cli,image-cli,ms-todo-cli,simplefin-cli,paddleocr}` | OpenCode |
| Container PATH configuration | `~/source/repos/swarm/deploy/leankernel/docker-stack.yml` (`PATH: "/app/data/skills/bin:..."`) | OpenCode |
| Existing egress and governance | `src/Common/LeanKernel.Logic/Tools/Dynamic/EgressValidator.cs`, `src/Common/LeanKernel.Logic/Tools/ToolGovernancePolicy.cs` | OpenCode |
| Current HTTP-only validation in LoadSkillFile | `src/Common/LeanKernel.Logic/Extensions/IServiceProviderExtensions.cs:279-305` (BaseUrl check, host extraction, bearer secretRef validation) | OpenCode |
| Live docs for tool runtime | `docs/features/tool-runtime.md`, `docs/operations/tool-configuration.md` | OpenCode |

## Optional Inputs
- Existing `blog/SKILL.md` review-gate workflow (ms-todo integration) for end-to-end CLI skill verification.
- Existing `ms-todo-cli/SKILL.md` Microsoft Graph API integration for bearer auth + egress verification.

## Input Validation Checklist
- [ ] Old CLI execution code is reviewed as behavioral reference (not code-copy target)
- [ ] Existing SKILL.md format aligns with the planned CLI schema extension
- [ ] Container PATH includes `/app/data/skills/bin` for binary resolution
- [ ] Prebuilt binaries are functional linux-x64 ELF executables
- [ ] LoadSkillFile HTTP validation logic is understood for CLI bypass
- [ ] Live docs locations identified for CLI schema updates
