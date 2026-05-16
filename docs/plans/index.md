# Roadmap PRDs

This section contains execution-ready product requirements documents (PRDs) for each roadmap item listed in the README.

## Contents

| Document | Roadmap Item | Outcome |
| -------- | ------------ | ------- |
| [admin-console-customization-prd.md](admin-console-customization-prd.md) | Upgrade runtime settings, LiteLLM routing management, and onboarding UX | Safer customization with a more maintainable admin console |
| [autonomy-policy-engine-prd.md](autonomy-policy-engine-prd.md) | Add autonomy policy engine and per-tool approval gates | Safer rollout from suggest-only to controlled automation |
| [run-replay-provenance-prd.md](run-replay-provenance-prd.md) | Ship run replay, cost timeline, and context provenance views | Faster debugging and higher operator trust |
| [budget-guardrails-fallback-prd.md](budget-guardrails-fallback-prd.md) | Implement budget enforcement with graceful fallback routing | Predictable spend with resilient answer quality |
| [memory-hygiene-quality-prd.md](memory-hygiene-quality-prd.md) | Add memory hygiene and quality scoring pipelines | Higher retrieval accuracy and lower context pollution |
| [wiki-extraction-store-prd.md](wiki-extraction-store-prd.md) | Replace deterministic wiki extraction and add indexed wiki storage | Human-readable wiki facts with indexed and Qdrant-ready retrieval |
| [benchmark-scenarios-prd.md](benchmark-scenarios-prd.md) | Publish benchmark scenarios with reproducible metrics | Clear ROI and objective quality tracking |
| [wiki-knowledge-tool-unification.md](wiki-knowledge-tool-unification.md) | Unify free-text wiki retrieval on Qdrant and add exact wiki entry lookup | Consistent semantic discovery plus deterministic wiki hydration |
| [entity-discovery-useful-by-default-prd.md](entity-discovery-useful-by-default-prd.md) | Improve gatekeeper entity discovery and contextual linking for people/org references | Useful-by-default responses with richer person + organization grounding |

## Planning Conventions

All PRDs in this folder follow a consistent structure:

- Overview and problem statement
- Goals and non-goals
- User stories and requirements (functional and non-functional)
- Architecture and data model
- API/UI contracts
- Security and privacy constraints
- Telemetry and success metrics
- Rollout phases and acceptance criteria
- Implementation clarifications and sprint-ready engineering tickets
- Risks, dependencies, and open questions

## Delivery Order

Recommended implementation order:

1. `autonomy-policy-engine-prd.md`
2. `run-replay-provenance-prd.md`
3. `budget-guardrails-fallback-prd.md`
4. `memory-hygiene-quality-prd.md`
5. `wiki-extraction-store-prd.md`
6. `benchmark-scenarios-prd.md`

This order reduces operational risk by adding control and observability before increasing automation breadth.
