# Product Requirements Document (PRD)

## Product Name
**ContextKernel**

## Product Description
A personal AI agent platform for builders who want reliable output, lower token spend, and full control of context.

## Document Purpose
Define the requirements for a brand new personal AI agent platform that preserves the exact feature set and differentiation of the predecessor system while aligning as closely as possible with native Microsoft Agent Framework (MAF) concepts and artifacts.

---

# 1. Executive Summary

ContextKernel is a greenfield personal AI agent platform built for builders who need:

- reliable output
- lower token spend
- full control of context

The implementation must preserve the same effective feature set as the predecessor system, including:

- deny-by-default context gating
- deterministic token budgeting
- structured identity and onboarding
- scoped knowledge retrieval
- tool governance
- multi-agent orchestration
- response quality gates
- model routing and escalation
- post-turn learning/self-improvement hooks
- rich diagnostics and auditability

The system should use **native MAF artifacts by default** and only introduce custom implementation when necessary to preserve product-defining deterministic behavior.

This is a **new product implementation**, not a refactor.

---

# 2. Vision

Build a personal AI agent that is:

- **Reliable** in what it says and does
- **Efficient** in token and model spend
- **Controllable** in context, tools, and autonomy
- **Framework-aligned** through first-class use of MAF concepts
- **Extensible** for future agents, tools, workflows, and retrieval sources

---

# 3. Core Value Proposition

ContextKernel exists for builders who want:

1. **Reliable output**  
   The system must prefer verified, deterministic behavior over opaque convenience.

2. **Lower token spend**  
   The system must aggressively manage prompt size and retrieval inclusion.

3. **Full control of context**  
   The system must make context admission, exclusion, and shaping explicit and inspectable.

---

# 4. Goals

## 4.1 Primary Goals

1. Preserve the exact effective feature set and product differentiation of the predecessor system.
2. Default to native MAF concepts and implementation patterns.
3. Retain all deterministic behaviors that materially support reliability, cost control, and context control.
4. Minimize custom code to what is strictly necessary.
5. Improve architectural alignment with the base framework.

## 4.2 Secondary Goals

1. Reduce framework impedance.
2. Improve contributor ergonomics.
3. Improve observability.
4. Enable easier future expansion into workflows, routing, and multi-agent execution.

---

# 5. Non-Goals

1. Reproducing legacy namespaces or class names.
2. Maintaining backward compatibility with old internal APIs.
3. Building a generic agent SDK.
4. Supporting every external channel in the first release.
5. Replacing deterministic behavior with opaque framework defaults.

---

# 6. Users

## Primary Users
Builders and technical operators who want a personal AI agent with strong context control, deterministic behavior, and efficient model usage.

## Secondary Users
Developers and maintainers who want a framework-aligned system that is easier to extend and reason about.

---

# 7. Product Principles

## 7.1 Native-First
Use native MAF artifacts and concepts unless they fail to preserve product goals.

## 7.2 Deterministic Where It Matters
The following must remain deterministic and auditable:

- context admission
- budget allocation
- retrieval scope
- history compaction
- tool eligibility
- quality gates
- escalation rules
- final instruction composition

## 7.3 Context Is a Product Feature
Context selection is not an implementation detail. It is a core product capability.

## 7.4 Reliability Over Cleverness
The system must favor predictable behavior over abstraction purity.

## 7.5 Custom Only by Exception
Custom implementation is allowed only when native MAF concepts cannot satisfy a required product behavior.

---

# 8. Feature Parity Requirements

The new system must match the predecessor system’s feature set, including:

- personal AI agent behavior
- MAF-backed agent runtime
- chat-based execution
- middleware support
- structured system prompt construction
- user and agent identity grounding
- onboarding gap detection
- deny-by-default context gating
- token-budgeted context assembly
- conversation history compaction
- wiki-style structured memory
- vector/knowledge retrieval
- scoped knowledge filtering
- tool adaptation and governance
- agent-specific tool filtering
- worker-agent orchestration
- workflow-based orchestration
- static model invocation
- routed model invocation
- shadow routing
- quality gating
- response enhancement
- post-turn persistence
- self-improvement hooks/pipeline
- capability gap handling
- diagnostics and audit logging

---

# 9. Product Requirements

## FR-1: Native MAF Runtime Core

The system must use native MAF agent concepts as the default execution path.

### Requirements
- Use native MAF agent artifacts for execution.
- Use native MAF sessions where possible.
- Use native MAF tools/functions for callable skills.
- Use native MAF workflows for orchestration where appropriate.

### Acceptance Criteria
- A single-turn user request can be executed by a native MAF agent.
- Tool invocation occurs through native MAF-compatible tools/functions.
- Multi-agent orchestration can be built with native MAF workflow or agent-as-tool concepts.

---

## FR-2: Deterministic Context Control

The system must preserve deny-by-default context admission with explicit budget-aware selection.

### Requirements
- Start from an empty context window.
- Use native MAF context/provider concepts where possible.
- Apply deterministic selection over candidate context.
- Preserve structured exclusion reasons.
- Allow scoped retrieval constraints.

### Acceptance Criteria
- Context is not automatically included unless admitted by policy.
- Included and excluded items are inspectable.
- Retrieval candidates can be merged and filtered under deterministic rules.

---

## FR-3: Token Budgeting and Headroom

The system must enforce deterministic token budgets for prompt assembly.

### Requirements
- Reserve configurable response headroom.
- Allocate budget slices for:
  - system instructions
  - identity/profile context
  - history
  - retrieved knowledge
  - tool metadata
  - task overlays
- Prevent budget overflow.

### Acceptance Criteria
- Final model input remains within configured budget.
- Per-category budget usage is observable.
- Budget overflow triggers deterministic exclusion/truncation behavior.

---

## FR-4: Deterministic History Handling

The system must shape history predictably while minimizing token waste.

### Requirements
- Recent turns remain verbatim.
- Older turns are compacted deterministically.
- Very old turns are summarized or truncated by policy.
- The current message must not be duplicated.

### Acceptance Criteria
- The history strategy is configurable and auditable.
- Compacted turns are marked or traceable.
- The current query is included exactly once.

---

## FR-5: Identity and Onboarding

The system must support durable identity and preference grounding.

### Requirements
- Maintain agent identity and user preference artifacts.
- Detect missing or weak identity information.
- Inject onboarding guidance on first-session or gap conditions.
- Produce structured identity updates.

### Acceptance Criteria
- Missing identity data can trigger a guided onboarding directive.
- Identity context participates in instruction construction.
- Identity updates can be parsed and persisted safely.

---

## FR-6: Structured System Instruction Construction

The system must construct instructions from well-defined inputs rather than ad hoc string concatenation alone.

### Requirements
- Build final instructions from:
  - base system policy
  - agent identity
  - user preferences
  - onboarding directives
  - retrieved structured knowledge
  - retrieved semantic knowledge
  - tool visibility summary
  - task-specific overlays
- Prefer native MAF instruction/context mechanisms first.
- Add custom composition only when necessary to preserve determinism and diagnostics.

### Acceptance Criteria
- The system can emit a structured manifest of final instruction segments.
- Each segment has a known source.
- Final instructions are inspectable before model invocation.

---

## FR-7: Scoped Knowledge Retrieval

The system must support both structured memory and semantic retrieval with deterministic scoping.

### Requirements
- Support wiki-style structured facts.
- Support semantic/vector retrieval.
- Support agent or task scoped retrieval boundaries.
- Preserve retrieval source attribution and ranking metadata.

### Acceptance Criteria
- Agents retrieve only allowed knowledge.
- Structured and semantic results can be merged into the final context.
- Retrieval diagnostics show what was considered and selected.

---

## FR-8: Tool Governance and Exposure

The system must expose tools through native MAF concepts while preserving strict policy controls.

### Requirements
- Represent tools as MAF-native functions/tools.
- Filter tool visibility by:
  - tool name
  - category
  - agent role
  - autonomy policy
- Support optional authorization before execution.
- Record tool visibility and usage diagnostics.

### Acceptance Criteria
- Different agents can receive different tool sets.
- Tool exposure is deterministic and inspectable.
- Tool calls can be logged and audited.

---

## FR-9: Multi-Agent and Workflow Orchestration

The system must support coordinator/worker orchestration using native MAF concepts wherever possible.

### Requirements
- Support agent-as-tool patterns.
- Support workflow-based execution for parallel or staged reasoning.
- Ensure worker-specific tool and context scope.
- Preserve orchestration traceability.

### Acceptance Criteria
- A coordinator can delegate to specialized worker agents.
- Worker permissions and context visibility are constrained.
- Orchestration traces identify which worker contributed to the result.

---

## FR-10: Reliable Output Controls

The system must enforce quality and fallback behavior for reliability-sensitive outputs.

### Requirements
- Support response quality gates.
- Support retry/escalation to larger or more suitable models.
- Support model routing based on:
  - prompt complexity
  - context size
  - tool usage
  - cost policy
  - quality risk
- Emit routing diagnostics.

### Acceptance Criteria
- Responses can be rejected by deterministic quality policy.
- Escalation can occur when configured rules fail.
- Routing decisions are explainable and logged.

---

## FR-11: Response Enhancement

The system must support synchronous response enhancement before the final answer is returned.

### Requirements
- Allow post-generation enhancement based on context or retrieved knowledge.
- Ensure enhancements do not violate deterministic guardrails.
- Preserve observability of enhancements applied.

### Acceptance Criteria
- Responses can be enriched before delivery.
- Enhancement steps are traceable.

---

## FR-12: Post-Turn Persistence and Learning

The system must support post-turn processing without blocking the main user experience more than necessary.

### Requirements
- Persist user and assistant turns.
- Publish structured turn events.
- Support background self-improvement or learning pipelines.
- Track capability gaps and operational misses.

### Acceptance Criteria
- Turn data is durable.
- Post-turn learning hooks can run independently of the user-facing response.
- Capability gaps can be recorded for future improvement.

---

## FR-13: Diagnostics and Auditability

The system must expose comprehensive diagnostics for context, instructions, tools, routing, and outputs.

### Requirements
- Emit diagnostics for:
  - token budgets
  - included/excluded context
  - history shaping
  - final instruction segments
  - visible tools
  - tool calls
  - routing choices
  - quality-gate outcomes
  - enhancement steps
- Make diagnostics available for logs and optional UI inspection.

### Acceptance Criteria
- Developers can inspect why an answer was produced.
- Diagnostic artifacts are structured and machine-readable.
- The system can surface a compact human-readable diagnostic summary.

---

# 10. Non-Functional Requirements

## NFR-1: Performance
The system must remain responsive for interactive usage.

## NFR-2: Cost Efficiency
The system must minimize unnecessary token and model spend.

## NFR-3: Extensibility
The system must support new agents, tools, and retrieval sources without major redesign.

## NFR-4: Maintainability
Custom implementations must be justified and minimized.

## NFR-5: Safety and Governance
The system must enforce action boundaries and tool policies.

---

# 11. Proposed Architecture

## 11.1 Architectural Pattern
**MAF-native execution with deterministic policy overlays**

## 11.2 Native-First Artifacts
The platform should default to native MAF concepts for:
- agents
- sessions
- tools/functions
- workflows
- middleware
- context providers and related artifacts where available and suitable

## 11.3 Custom Components Only When Necessary
Custom components may be introduced for:
- deterministic context coordination
- budget enforcement
- context inclusion/exclusion audit manifests
- scoped retrieval overlays
- identity/onboarding policy handling
- quality gating and escalation policy
- deterministic history compaction if native behavior is insufficient

---

# 12. Native-vs-Custom Decision Rule

Use native MAF implementation if it:
- satisfies the requirement
- preserves determinism where required
- remains auditable
- does not materially worsen token efficiency

Use custom implementation only if:
- native MAF cannot enforce the needed policy
- native MAF cannot preserve sufficient observability
- native MAF cannot preserve scoped/deterministic behavior
- custom logic is required to preserve the core product value proposition

---

# 13. Required Deterministic Behaviors

The new implementation must retain the following deterministic behaviors:

1. deny-by-default context admission
2. token budget slicing
3. reserved response headroom
4. deterministic history shaping
5. scoped knowledge access
6. structured onboarding and identity prompting
7. deterministic tool eligibility
8. response quality gating
9. escalation and fallback policy
10. auditable context inclusion/exclusion logs
11. inspectable final instruction construction

---

# 14. Success Metrics

## Product Success
- high trust in agent output
- reduced irrelevant-context failures
- lower average token consumption
- improved user satisfaction for builder workflows

## Engineering Success
- greater use of native MAF concepts than the predecessor implementation
- smaller custom orchestration surface area
- easier contributor onboarding
- lower maintenance burden

## Reliability Success
- high rate of explainable responses
- high quality-gate pass rate
- successful escalation when first-pass responses fail policy

---

# 15. Phased Delivery

## Phase 1: Core Runtime
- native MAF agent runtime
- sessions
- tool exposure
- basic deterministic context coordination
- instruction manifesting
- static invocation path

## Phase 2: Context and Personalization
- identity grounding
- onboarding gaps
- scoped retrieval
- deterministic history shaping
- context diagnostics

## Phase 3: Reliability and Optimization
- model routing
- shadow routing
- quality gates
- escalation policies
- workflow orchestration
- response enhancement
- post-turn learning

---

# 16. Risks

## Risk 1
Overusing native abstractions could weaken product differentiation.

### Mitigation
Preserve all required deterministic features and treat them as non-negotiable.

## Risk 2
Too much custom overlay could recreate the predecessor architecture.

### Mitigation
Require a written native-first justification for each custom subsystem.

## Risk 3
Token efficiency could regress.

### Mitigation
Make budget enforcement hard, deterministic, and measurable.

## Risk 4
Prompt construction could become fragmented.

### Mitigation
Require a single authoritative final instruction manifest before invocation.

---

# 17. Open Questions

1. Which native MAF context-provider patterns can fully support the required retrieval and history use cases?
2. Should identity artifacts remain file-backed or move to a structured store/provider representation?
3. What is the minimum custom surface needed to preserve all deterministic behaviors?
4. How much of instruction construction can be represented through native framework composition without losing transparency?
5. Which diagnostics should be end-user visible vs developer-only?

---

# 18. Final Requirement Statement

ContextKernel must be a MAF-native personal AI agent platform that preserves the full functional feature set and differentiation of the predecessor system while improving alignment with the base framework. It must default to native MAF artifacts and concepts, introducing custom implementation only when necessary to preserve deterministic context control, token efficiency, reliability, scoped retrieval, tool governance, and auditability.
