# Documentation Inventory Matrix

This inventory tracks documentation status and canonical targets for the current refresh.

Status values:

- `accurate`: implementation-aligned and canonical
- `partial`: mostly accurate but still needs consolidation/cleanup
- `stale`: historical or no longer implementation-accurate
- `duplicate`: kept for compatibility; canonical content exists elsewhere

## Repository Docs (Non-Plan)

| Path | Audience | Status | Canonical Target |
| --- | --- | --- | --- |
| `docs/index.md` | all contributors | accurate | self |
| `docs/CONTRIBUTING-DOCS.md` | doc contributors | partial | `docs/development/docs-style-guide.md` |
| `docs/channel-consolidation.md` | maintainers | partial | `docs/features/channels/index.md` |
| `docs/getting-started/index.md` | new contributors | accurate | self |
| `docs/getting-started/quick-start.md` | new contributors | accurate | self |
| `docs/getting-started/local-development.md` | developers | accurate | self |
| `docs/getting-started/local-testing.md` | developers | accurate | self |
| `docs/getting-started/troubleshooting-startup.md` | developers/operators | accurate | self |
| `docs/api/index.md` | integrators | accurate | self |
| `docs/api/gateway-api.md` | integrators | accurate | self |
| `docs/api/diagnostics-api.md` | integrators/operators | accurate | self |
| `docs/api/host-api.md` | integrators/developers | accurate | self |
| `docs/architecture/index.md` | developers | accurate | self |
| `docs/architecture/system-overview.md` | developers | accurate | self |
| `docs/architecture/runtime-flows.md` | developers | accurate | self |
| `docs/architecture/data-and-persistence.md` | developers | accurate | self |
| `docs/architecture/infrastructure-and-deploy.md` | developers/operators | accurate | self |
| `docs/architecture/solution-structure.md` | developers | accurate | self |
| `docs/architecture/overview.md` | developers | duplicate | `docs/architecture/system-overview.md` |
| `docs/architecture/architecture.md` | developers | duplicate | `docs/architecture/system-overview.md` |
| `docs/architecture/key-flows.md` | developers | duplicate | `docs/architecture/runtime-flows.md` |
| `docs/architecture/infrastructure.md` | developers/operators | duplicate | `docs/architecture/infrastructure-and-deploy.md` |
| `docs/architecture/data-model.md` | developers | duplicate | `docs/architecture/data-and-persistence.md` |
| `docs/architecture/gaps-and-roadmap.md` | maintainers | stale | `docs/plans/index.md` |
| `docs/features/index.md` | developers | accurate | self |
| `docs/features/agent-runtime/index.md` | developers | accurate | self |
| `docs/features/channels/index.md` | developers | accurate | self |
| `docs/features/ui/index.md` | developers | accurate | self |
| `docs/features/tools/index.md` | developers | accurate | self |
| `docs/features/skills.md` | developers | accurate | self |
| `docs/features/middleware.md` | developers | accurate | self |
| `docs/features/gateway-api.md` | developers | duplicate | `docs/api/gateway-api.md` |
| `docs/features/context-diagnostics-api.md` | developers | duplicate | `docs/api/diagnostics-api.md` |
| `docs/features/production-ops.md` | operators | duplicate | `docs/operations/production-ops.md` |
| `docs/features/*.md` (other feature pages) | developers | partial | capability indexes under `docs/features/*/index.md` |
| `docs/configuration/index.md` | operators/developers | accurate | self |
| `docs/configuration/configuration-reference.md` | operators/developers | accurate | self |
| `docs/configuration/environment-variables.md` | operators/developers | accurate | self |
| `docs/configuration/appsettings-reference.md` | operators/developers | accurate | self |
| `docs/configuration/phase-1-config.md` | maintainers | partial | `docs/configuration/configuration-reference.md` |
| `docs/configuration/phase-2-config.md` | maintainers | partial | `docs/configuration/configuration-reference.md` |
| `docs/configuration/phase-3-config.md` | maintainers | partial | `docs/configuration/configuration-reference.md` |
| `docs/development/index.md` | contributors | accurate | self |
| `docs/development/build-and-test.md` | contributors | accurate | self |
| `docs/development/docs-style-guide.md` | contributors | accurate | self |
| `docs/development/quality-gates.md` | contributors | accurate | self |
| `docs/development/quality.md` | contributors | partial | `docs/development/quality-gates.md` |
| `docs/development/litellm-spec.md` | contributors | partial | self |
| `docs/operations/index.md` | operators | accurate | self |
| `docs/operations/production-ops.md` | operators | accurate | self |
| `docs/operations/health-and-observability.md` | operators | accurate | self |
| `docs/skills/index.md` | contributors | accurate | self |
| `docs/skills/skill-format.md` | contributors | accurate | self |
| `docs/skills/runtime-skills.md` | contributors | accurate | self |
| `docs/skills/runtime-skills-plan.md` | maintainers | partial | `docs/skills/runtime-skills.md` |

## Plans Inventory

`docs/plans/**/*.md` is tracked through curated indexes:

- Active candidates: `docs/plans/active/index.md`
- Archived/historical: `docs/plans/archive/index.md`
- Back-compat master list: `docs/plans/index.md`

Current mapping rule:

- Files explicitly listed in `active/index.md` are `partial` or `accurate` depending on implementation state.
- Phase milestone and superseded PRDs listed in `archive/index.md` are `stale` or `historical`.
- Any plan not yet curated in active/archive remains `partial` and temporarily discovered via `docs/plans/index.md`.
