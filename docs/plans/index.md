# Roadmap PRDs

This section contains execution-ready product requirements documents (PRDs) for roadmap work, plus a small number of companion planning artifacts such as architecture decision records.

## Contents

| Document | Roadmap Item | Outcome |
| -------- | ------------ | ------- |
| [architecture-decisions.md](architecture-decisions.md) | Record accepted rearchitecture decisions for native-vs-custom design choices | Shared decision log for the LeanKernel rebuild |
| [phase-0-documentation-convention.md](phase-0-documentation-convention.md) | Establish the rearchitecture documentation contract for features, API docs, configuration references, and README hygiene | Consistent contributor-facing documentation structure before Phase 1 implementation |
| [admin-console-customization-prd.md](admin-console-customization-prd.md) | Upgrade runtime settings, LiteLLM routing management, and onboarding UX | Safer customization with a more maintainable admin console |
| [phase-1-core-runtime.md](phase-1-core-runtime.md) | Rebuild LeanKernel as a MAF-native core runtime with deterministic context gating and Postgres durability | Working Phase 1 runtime foundation for the rearchitecture |
| [phase-2-context-personalization.md](phase-2-context-personalization.md) | Deliver Phase 2 of the LeanKernel rearchitecture: identity grounding, scoped retrieval, deterministic history shaping, diagnostics, and channels | API-first personalization and inspectable context control on the Phase 1 runtime |
| [phase-2-identity-onboarding-prd.md](phase-2-identity-onboarding-prd.md) | Implement the Phase 2 identity and onboarding slice on the current runtime | GBrain-backed identity grounding, additive onboarding, and best-effort identity writeback |
| [phase-2-channel-expansion-prd.md](phase-2-channel-expansion-prd.md) | Implement the Phase 2 channel abstraction, router, and Signal adapter slice | Shared inbound channel routing with fail-closed auth and disabled-by-default Signal support |
| [phase-2-deterministic-history-shaping-prd.md](phase-2-deterministic-history-shaping-prd.md) | Implement the deterministic history shaping slice of Phase 2 with tiered compaction, LiteLLM summarization, and persisted markers | Budget-aware and traceable conversation history shaping for Phase 2 |
| [autonomy-policy-engine-prd.md](autonomy-policy-engine-prd.md) | Add autonomy policy engine and per-tool approval gates | Safer rollout from suggest-only to controlled automation |
| [run-replay-provenance-prd.md](run-replay-provenance-prd.md) | Ship run replay, cost timeline, and context provenance views | Faster debugging and higher operator trust |
| [budget-guardrails-fallback-prd.md](budget-guardrails-fallback-prd.md) | Implement budget enforcement with graceful fallback routing | Predictable spend with resilient answer quality |
| [memory-hygiene-quality-prd.md](memory-hygiene-quality-prd.md) | Add memory hygiene and quality scoring pipelines | Higher retrieval accuracy and lower context pollution |
| [wiki-extraction-store-prd.md](wiki-extraction-store-prd.md) | Replace deterministic wiki extraction and add indexed wiki storage | Human-readable wiki facts with indexed and Qdrant-ready retrieval |
| [gbrain-embedding-dimension-prd.md](gbrain-embedding-dimension-prd.md) | Align GBrain embedding model dimensions with Postgres pgvector schema | Reliable GBrain page writes for learned facts and knowledge pages |
| [benchmark-scenarios-prd.md](benchmark-scenarios-prd.md) | Publish benchmark scenarios with reproducible metrics | Clear ROI and objective quality tracking |
| [p1-diagnostics-prd.md](p1-diagnostics-prd.md) | Implement the LeanKernel.Diagnostics rearchitecture slice for tracing, metrics, and log enrichment | Concrete observability package surface for the Phase 1 rearchitecture |
| [p1-context-prd.md](p1-context-prd.md) | Implement the LeanKernel.Context rearchitecture slice for deterministic token estimation, deny-by-default gating, and prompt assembly | Concrete context selection and prompt-budget package surface for the Phase 1 rearchitecture |
| [p1-tools-prd.md](p1-tools-prd.md) | Implement the LeanKernel.Tools rearchitecture slice for tool governance, registry, execution, and built-in wiki tools | Concrete tool runtime surface for Phase 1 orchestration |
| [useful-by-default-file-tools-prd.md](useful-by-default-file-tools-prd.md) | Add built-in file-system tools, OCR text extraction, and download-to-extract web fetch support | Useful-by-default local file access and non-text content processing |
| [document-folder-ingestion-monitor-prd.md](document-folder-ingestion-monitor-prd.md) | Add a Docker-friendly document folder monitor for automatic ingestion from a bind-mounted document tree | Drop-file document imports from `./data/documents` without upload-loop duplication |
| [tools-subnamespace-reorganization-prd.md](tools-subnamespace-reorganization-prd.md) | Reorganize built-in tools into grouped sub-folders and sub-namespaces | Cleaner tools code structure with stable behavior |
| [tools-1to4-max-usefulness-prd.md](tools-1to4-max-usefulness-prd.md) | Implement high-impact built-in tools: `http_request`, `json_transform`, `csv_xlsx_read_write`, and `database_query` | Default tooling surface for API calls, structured transforms, tabular files, and safe DB reads |
| [browser-built-in-tool-playwright-service-prd.md](browser-built-in-tool-playwright-service-prd.md) | Add LeanKernel `browser_*` built-in tools backed by a Webwright sidecar service that drives Playwright and routes LLM calls through LiteLLM | Browser automation via Microsoft's evaluated Webwright agent loop, exposed as deterministic submit-then-poll tools with LiteLLM-scoped spend controls and observability |
| [p1-agents-prd.md](p1-agents-prd.md) | Implement the LeanKernel.Agents rearchitecture slice for LiteLLM-backed runtime execution and turn orchestration | Concrete MAF-native agent runtime surface for the Phase 1 rearchitecture |
| [p1-gateway-prd.md](p1-gateway-prd.md) | Implement the LeanKernel.Gateway composition root and Minimal API entry point for Phase 1 | Concrete HTTP entry point, subsystem wiring, and API surface for the rearchitecture |
| [p1-infrastructure-prd.md](p1-infrastructure-prd.md) | Replace the local Docker Compose stack for the rearchitecture | Current four-service local runtime with Gateway, Postgres, LiteLLM, and GBrain |
| [p1-persistence-prd.md](p1-persistence-prd.md) | Implement the PostgreSQL-backed persistence layer for Phase 1 | Durable EF Core session, turn, and diagnostics storage for the rearchitecture |
| [p1-test-coverage-prd.md](p1-test-coverage-prd.md) | Raise Phase 1 rearchitecture test coverage to at least 80% | Comprehensive automated validation across Phase 1 runtime packages and gateway endpoints |
| [phase-1-feature-documentation-prd.md](phase-1-feature-documentation-prd.md) | Publish the remaining Phase 1 feature explanation docs | Complete understanding-oriented documentation for the implemented core runtime |
| [phase-2-feature-documentation-prd.md](phase-2-feature-documentation-prd.md) | Publish the Phase 2 feature and configuration explanation docs | Complete implementation-accurate documentation for Phase 2 context and personalization features |
| [wiki-knowledge-tool-unification.md](wiki-knowledge-tool-unification.md) | Unify free-text wiki retrieval on Qdrant and add exact wiki entry lookup | Consistent semantic discovery plus deterministic wiki hydration |
| [entity-discovery-useful-by-default-prd.md](entity-discovery-useful-by-default-prd.md) | Improve gatekeeper entity discovery and contextual linking for people/org references | Useful-by-default responses with richer person + organization grounding |
| [ambiguity-reference-resolution-prd.md](ambiguity-reference-resolution-prd.md) | Expand ambiguity handling across relation/pronoun and cross-source collisions | Safer identity grounding with clarify-first behavior under low confidence |
| [scheduled-jobs-management-prd.md](scheduled-jobs-management-prd.md) | Implement runtime scheduler management and chat CRUD tooling with OpenClaw-compatible capabilities | Scoped-by-default scheduled job management with admin governance and durable runtime state |
| [phase-3-scheduled-jobs-proactive-tasks-prd.md](phase-3-scheduled-jobs-proactive-tasks-prd.md) | Implement the Phase 3 scheduler runtime for cron jobs, proactive prompts, and maintenance work | Disabled-by-default proactive scheduling with persisted execution history and bounded background execution |
| [phase-3-reliability-optimization.md](phase-3-reliability-optimization.md) | Deliver Phase 3 of the LeanKernel rearchitecture: routing, orchestration, learning, and production hardening | Production-ready reliability and optimization across backend agent execution |
| [phase-3-multi-agent-orchestration-prd.md](phase-3-multi-agent-orchestration-prd.md) | Implement the Phase 3 coordinator-worker orchestration slice on the current runtime | Disabled-by-default worker delegation with scoped tools and structured contribution traces |
| [phase-3-quality-gates-prd.md](phase-3-quality-gates-prd.md) | Implement deterministic response quality gates with bounded model escalation | Auditable pre-delivery quality checks for Phase 3 routed execution |
| [phase-3-shadow-routing-prd.md](phase-3-shadow-routing-prd.md) | Implement Phase 3 shadow routing for parallel comparison without changing user-visible output | Non-authoritative shadow model execution with persisted comparison diagnostics |
| [phase-3-response-enhancement-prd.md](phase-3-response-enhancement-prd.md) | Implement Phase 3 synchronous response enhancement before delivery | Deterministic pre-delivery enhancement with traceable step results and timeout fallback |
| [phase-3-feature-documentation-prd.md](phase-3-feature-documentation-prd.md) | Publish the remaining Phase 3 feature and configuration explanation docs | Complete implementation-accurate documentation for Phase 3 reliability and optimization features |
| [phase-4-user-interface.md](phase-4-user-interface.md) | Deliver the full Blazor Server UI layer for the rearchitecture Phase 4 milestone | Complete daily-use product surface for chat, diagnostics, admin, onboarding, and knowledge browsing |
| [phase-4-blazor-chat-interface-prd.md](phase-4-blazor-chat-interface-prd.md) | Implement the foundational Phase 4 chat shell in `LeanKernel.Gateway` | Interactive Blazor Server chat workspace, layout, and navigation baseline for the rest of the UI |
| [onboarding-wizard-ui-prd.md](onboarding-wizard-ui-prd.md) | Implement the guided onboarding wizard in `LeanKernel.Gateway` | Interactive identity and preference setup that writes durable GBrain wiki profile pages |
| [onboarding-fluentui-v4-prd.md](onboarding-fluentui-v4-prd.md) | Rewrite the Gateway onboarding page markup to Microsoft FluentUI v4 components | Fluent-styled onboarding experience without changing existing onboarding logic |

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
