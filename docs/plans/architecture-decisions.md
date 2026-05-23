# Architecture Decisions — LeanKernel Rearchitecture

This document records the accepted architecture decisions for the LeanKernel rearchitecture project.

LeanKernel is being rebuilt from scratch as a MAF-native personal AI agent platform that preserves deny-by-default context gating and deterministic behavior.

This file is a target-state decision log and companion to [`rearchitecture.md`](rearchitecture.md) and [`phase-1-core-runtime.md`](phase-1-core-runtime.md). Existing architecture documents in this repository still describe the legacy system in places; the ADRs below capture the accepted direction for the rebuild.

## Decision principles

- Prefer native Microsoft Agent Framework (MAF) concepts and runtime artifacts by default.
- Add custom implementation only when native behavior cannot preserve deterministic, auditable product requirements.
- Treat context admission, history shaping, routing, and quality validation as product behaviors, not incidental framework details.
- Favor fewer subsystems and clearer operational boundaries when native or proven components can replace bespoke infrastructure.

---

## ADR-001

**Title:** Use MAF (Microsoft Agent Framework) for Agent Runtime  
**Status:** Accepted

### Context
LeanKernel needs an agent execution environment for turn processing, sessions, middleware, tool integration, and future multi-agent behaviors.

### Decision
Use native MAF `AIAgent`, sessions, and middleware as the default agent runtime foundation.

### Rationale
MAF v1.6.1 provides a production-ready agent runtime with OpenTelemetry integration, a middleware pipeline, and multi-agent support. Using the framework runtime reduces custom orchestration code and keeps the new platform aligned with native MAF concepts.

### Consequences
#### Positive
- Reduces bespoke runtime and orchestration code.
- Reuses MAF session, middleware, and observability primitives.
- Creates a clearer path to native tool and workflow support.

#### Negative
- Introduces dependency on MAF API evolution and release cadence.
- Requires the team to work within MAF runtime constraints where product behavior allows.
- Does not remove the need for custom policy layers in product-defining areas.

---

## ADR-002

**Title:** Custom Context Gating (Not MAF Default)  
**Status:** Accepted

### Context
MAF provides context and provider concepts, but LeanKernel requires deny-by-default admission with deterministic budget enforcement and auditable inclusion decisions.

### Decision
Implement a custom `ContextGatekeeper` with deterministic token budgeting and explicit context admission rules.

### Rationale
This is the core product differentiator. MAF's default context handling is permissive and non-deterministic, while LeanKernel's product value depends on explicit, inspectable, and auditable context admission.

### Consequences
#### Positive
- Preserves LeanKernel's deny-by-default context model.
- Makes budget usage and inclusion or exclusion decisions inspectable.
- Supports deterministic tests and repeatable prompt assembly.

#### Negative
- Increases the custom code surface that must be maintained and tested.
- Requires tokenizer and budgeting behavior to remain stable across releases.
- Limits how much default framework context behavior can be adopted unchanged.

---

## ADR-003

**Title:** GBrain for Knowledge/Memory (Replace Qdrant + Custom Wiki)  
**Status:** Accepted

### Context
The current system combines a custom wiki store, vector search via Qdrant, and entity extraction logic across separate subsystems.

### Decision
Use the GBrain MCP server for all knowledge operations, including wiki, hybrid search, and graph traversal.

### Rationale
GBrain provides a markdown-backed wiki with automatic Postgres sync, hybrid search (`pgvector` + `tsvector`), entity graph traversal, and dream cycles. It replaces three or more custom subsystems with one proven tool and minimizes custom code in line with the rearchitecture principles.

### Consequences
#### Positive
- Unifies wiki, search, and graph traversal behind one knowledge service.
- Reduces subsystem count and custom maintenance burden.
- Keeps knowledge human-readable while improving retrieval capability.

#### Negative
- Makes GBrain a critical dependency for knowledge workflows.
- Requires migration of existing wiki, retrieval, and entity behaviors.
- Couples knowledge capabilities to GBrain interface stability and availability.

---

## ADR-004

**Title:** Postgres as Single Source of Truth  
**Status:** Accepted

### Context
The current system relies heavily on file-based storage such as JSON sessions, markdown wiki pages, and YAML rules.

### Decision
Use Postgres 16 with `pgvector` as the authoritative system of record for sessions, turns, jobs, and diagnostics. GBrain will sync wiki content to and from Postgres automatically.

### Rationale
A single authoritative store removes many consistency problems caused by file-based persistence. Postgres enables transactions, indexing, querying, migrations, and simpler operational backup and recovery patterns.

### Consequences
#### Positive
- Eliminates split persistence for core runtime state.
- Improves transactional consistency and queryability.
- Simplifies migrations, backup strategy, and operational troubleshooting.

#### Negative
- Increases reliance on database schema design and migration discipline.
- Concentrates performance and availability risk in the primary database.
- Requires stronger retention, governance, and operational tuning practices.

---

## ADR-005

**Title:** Identity Merged Into GBrain Wiki  
**Status:** Accepted

### Context
The current system stores identity and profile data separately in files such as engagement YAML and user preference JSON.

### Decision
Merge identity into GBrain wiki pages, using one page per user or agent profile.

### Rationale
This simplifies context assembly by using a single retrieval path, leverages GBrain cross-referencing, and keeps identity data human-readable as markdown. It also removes custom file parsing and duplication across identity-related subsystems.

### Consequences
#### Positive
- Creates one retrieval path for knowledge and identity grounding.
- Makes identity artifacts easier to inspect and edit as markdown.
- Enables cross-links between identity, memory, and related facts.

#### Negative
- Identity content requires stricter privacy, provenance, and retention controls than general knowledge.
- Requires schema conventions so profile pages stay predictable for retrieval.
- Blends sensitive profile data into the broader knowledge system if governance is weak.

---

## ADR-006

**Title:** Domain-Driven Solution Structure  
**Status:** Accepted

### Context
The current modular monolith uses metaphor-based project names such as Thinker, Archivist, and Commander, which can make responsibilities harder to discover for new contributors.

### Decision
Reorganize the solution into domain-aligned projects: `Agents`, `Context`, `Knowledge`, `Gateway`, `Tools`, `Persistence`, and `Diagnostics`.

### Rationale
Domain-aligned names improve discoverability and map directly to responsibilities. This reduces confusion about where code belongs and makes the architecture easier to understand and extend.

### Consequences
#### Positive
- Improves contributor onboarding and codebase discoverability.
- Makes ownership boundaries more explicit.
- Aligns project names with runtime responsibilities instead of internal metaphors.

#### Negative
- Introduces migration and rename churn from the legacy structure.
- Requires documentation, namespace, and dependency updates across the solution.
- Can create too many seams if boundaries are drawn too narrowly.

---

## ADR-007

**Title:** Custom Model Routing (Not LiteLLM Router Alone)  
**Status:** Accepted

### Context
LiteLLM provides basic load-balancing and alias-based routing, but LeanKernel needs deterministic routing decisions based on task complexity, tool usage, cost policy, and quality risk.

### Decision
Implement a custom `PolicyModelSelector` and `TaskComplexityScorer` for routing decisions, while keeping LiteLLM as the model proxy.

### Rationale
The product requires deterministic and auditable routing based on factors that LiteLLM's built-in router does not score. Routing must be policy-driven, explainable, and logged rather than delegated entirely to proxy defaults.

### Consequences
#### Positive
- Preserves deterministic and auditable model selection.
- Allows routing policy to consider product-specific quality and cost signals.
- Keeps LiteLLM focused on proxying, provider compatibility, and model access.

#### Negative
- Adds another policy layer that must stay consistent with LiteLLM aliases and configuration.
- Increases testing and diagnostics requirements for routing behavior.
- Creates more custom logic than using proxy routing alone.

---

## ADR-008

**Title:** Custom Quality Gates and Escalation  
**Status:** Accepted

### Context
MAF does not provide deterministic response quality validation or policy-driven escalation behavior.

### Decision
Implement a custom `ResponseQualityGate` with deterministic checks and an `EscalationPolicy` for stronger-model retries when needed.

### Rationale
Reliability is a core product value. Responses must pass deterministic quality checks, and escalation to stronger models must be policy-driven, explainable, and logged.

### Consequences
#### Positive
- Improves response reliability and consistency.
- Makes escalation behavior inspectable and measurable.
- Supports policy-based trade-offs between cost and answer quality.

#### Negative
- Can add latency and cost when checks fail and escalation triggers.
- Risks false positives or false negatives if gate criteria are poorly tuned.
- Requires careful diagnostics so operators can understand why responses were rejected or escalated.

---

## ADR-009

**Title:** MAF Workflows for Multi-Agent Orchestration  
**Status:** Accepted

### Context
LeanKernel needs coordinator and worker patterns for complex tasks without reinventing orchestration infrastructure.

### Decision
Use MAF graph-based workflows and the agent-as-tool pattern for multi-agent orchestration.

### Rationale
MAF provides a production-ready workflow engine with a superstep BSP model. Using native workflows avoids rebuilding orchestration from scratch, and exposing workers as `AIFunction` tools follows the native framework pattern.

### Consequences
#### Positive
- Reuses native workflow and orchestration primitives.
- Improves consistency with MAF tooling and tracing.
- Keeps worker invocation aligned with the agent-as-tool pattern.

#### Negative
- Introduces workflow concepts that contributors must learn.
- Still requires LeanKernel policy overlays for tool governance, context, and auditability.
- May constrain some orchestration designs to framework-supported patterns.

---

## ADR-010

**Title:** API-First, UI Last (Phase 4)  
**Status:** Accepted

### Context
The previous system included a tightly coupled Blazor UI. The rearchitecture needs stable backend contracts before a new interface is built.

### Decision
Build all functionality as API-only in Phases 1 through 3, and add the UI in Phase 4 after backend contracts and runtime behavior are stable.

### Rationale
This ensures backend contracts are well-defined and testable independently of presentation concerns. It prevents UI-driven compromises in API design and allows UI work to proceed against stable interfaces.

### Consequences
#### Positive
- Forces clearer API contracts and backend boundaries.
- Improves automated testability and backend iteration speed.
- Allows later UI work to build on stable, documented interfaces.

#### Negative
- Delays operator and end-user UI feedback.
- Makes early demos and operational workflows more API-centric.
- Requires discipline to avoid leaking UI assumptions into backend design later.

---

## ADR-011

**Title:** Remove Qdrant (Use pgvector via GBrain)  
**Status:** Accepted

### Context
The current system uses Qdrant for vector similarity search as a separate infrastructure service.

### Decision
Remove Qdrant and use GBrain on top of `pgvector` within the same Postgres instance.

### Rationale
This reduces infrastructure services, removes sync issues between Postgres and a separate vector database, and keeps vector search within the same operational store. For personal-scale data, `pgvector` with HNSW indexes provides adequate performance.

### Consequences
#### Positive
- Reduces infrastructure count and operational overhead.
- Eliminates split-brain risks between relational and vector stores.
- Simplifies backup, restore, and local environment setup.

#### Negative
- Trades specialized vector-database features for a consolidated architecture.
- Requires vector index tuning and performance validation inside Postgres.
- Involves migration work from existing Qdrant-backed retrieval paths.

---

## ADR-012

**Title:** Custom History Compaction  
**Status:** Accepted

### Context
MAF does not provide deterministic, budget-aware history shaping that matches LeanKernel's context-control requirements.

### Decision
Implement a custom `ConversationHistoryAssembler` with a configurable compaction strategy.

### Rationale
Token efficiency requires predictable history degradation from verbatim to compacted to summarized forms. This behavior must be deterministic and budget-aware, which is not available as a framework default.

### Consequences
#### Positive
- Preserves predictable history inclusion under tight token budgets.
- Makes history shaping auditable and configurable.
- Supports repeatable prompt construction and cost control.

#### Negative
- Adds custom summarization and traceability logic to maintain.
- Requires careful testing to avoid losing important conversational state.
- Reduces the amount of default framework history behavior that can be used directly.
