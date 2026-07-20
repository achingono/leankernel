# Phase 19 Agent Personalization and Engagement Preferences — Outputs

## Mandatory Outputs

| Output | Description | Format |
| --- | --- | --- |
| `EngagementPreferenceSet` model + allowlist descriptor | Typed, bounded field set and the closed allowlist (name → type, allowed values, max length, default) with validation/sanitization helpers | C# in `LeanKernel.Logic` |
| `UserPreferenceEntity` + `TenantPreferenceDefaultsEntity` | Persisted per-identity and per-tenant preference storage following auditable/recyclable conventions | C# entities in `LeanKernel.Core/Entities` |
| EF Core migration + `EntityContext` wiring | New `DbSet`s, `OnModelCreating` config with unique identity index, generated migration | C# + migration files in `LeanKernel.Data` |
| `IEngagementPreferenceStore` | Repository that reads/writes preferences strictly by permit-derived tenant + identity scope | C# in `LeanKernel.Logic` |
| `EngagementInstructionComposer` (merge policy) | Field-level, deterministic four-layer merge (base → tenant → user → per-turn) producing a canonical, byte-stable instruction stack | C# in `LeanKernel.Logic` |
| Safety-constraint layer | Immutable, non-overridable core-policy segment applied outside/above the preference block, with clamp logic for conflicting fields | C# in `LeanKernel.Logic` |
| Deterministic preference renderer | Converts allowlisted fields into a stable ordered prompt block | C# in `LeanKernel.Logic` |
| Turn-pipeline integration | `TurnContext.ComposedInstructions` + `PerTurnPreferenceOverride` properties, `PreferenceCompositionStage` (loads via `TurnContext.Permit`, applies overrides, composes), and `PromptAssembler` consuming `ComposedInstructions` with fallback to `DefaultInstructions`; no new gated `ContextItem` | C# in `LeanKernel.Logic/TurnRuntime` |
| DI registration | `IEngagementPreferenceStore`, `EngagementInstructionComposer`, and `PreferenceCompositionStage` registered `Scoped` in `TurnPipelineServiceExtensions`, stage slotted after `HistoryShaper`, before `PromptAssembler` | C# in `LeanKernel.Logic/TurnRuntime/TurnPipelineServiceExtensions.cs` |
| Per-turn override transport + fallback | Bounded, allowlisted `engagement` request field validated at the invocation site and passed into `TurnContext`; specific store-failure fallback to base + safety with logged warning | C# at pipeline invocation site + `PreferenceCompositionStage` |
| Configuration + startup validation | `Agents:Personalization` options (enable/disable, allowlist bounds, tenant-default source) with fail-fast validation | C# + `appsettings` shape |
| Test suite | Deterministic composition, merge precedence, safety non-override, allowlist/sanitization, isolation, pipeline integration; ≥80% coverage | xUnit tests in `test/LeanKernel.Tests.Unit` (+ Integration where EF-backed) |
| Documentation | New feature page + configuration and architecture updates reflecting the composition path | Markdown in `docs/` |

## Optional Outputs
- Admission-trace/diagnostic fields recording contributing layers and clamped preferences (no sensitive values).
- A read API contract stub for a future preference-editing UI (Phase 09).

## Output Quality Checklist
- [ ] All mandatory outputs produced
- [ ] All outputs reviewed before gate
- [ ] Evidence log updated with output references
- [ ] Coverage ≥80% on new types
- [ ] Deterministic composition proven by repeated-run byte equality tests
