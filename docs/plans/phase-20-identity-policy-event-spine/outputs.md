# Phase 20 Outputs

## Mandatory Outputs

| Output | Description | Format |
| --- | --- | --- |
| Canonical identity contract | Identity model and invariants for tenant/person/user/channel/anonymous-session boundaries, including which runtime surfaces use each dimension | Markdown + C# |
| Shared policy core | `IPolicyContext`, `IPolicy<TEntity>`, `IPolicyEvaluator`, and default implementations that compose with `IPermit<TEntity>` / `IFilter<TEntity>` / `IRepository<TEntity>` | C# source |
| Event spine contracts | Append-only contracts plus event envelope and projection rules for turns, tool calls, and telemetry | C# source |
| First-adopter migration | One consumer path wired to the new policy/event core | C# source |
| Gateway guardrails | Thin-host composition rules and supporting tests | C# source |
| Design docs | Adoption notes, contract rules, and future-service decision rationale | Markdown |

## Optional Outputs
- Policy extension examples for future domain features.
- A deferred-service decision note for the policy core.
- A review addendum capturing alternative implementation choices considered.

## Output Quality Checklist
- [ ] All mandatory outputs produced
- [ ] All outputs reviewed before gate
- [ ] Evidence log updated with output references
