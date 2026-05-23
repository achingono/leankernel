# PRD: Phase 4 User Interface

## Overview

Build the complete LeanKernel user interface layer in `LeanKernel.Host` with Blazor Server, using the already-complete API backend and the stabilized capabilities delivered in Phases 1–3.

Phase 4 turns LeanKernel from a backend-complete MAF-native personal AI agent platform into a full daily-use product surface for chat, diagnostics, administration, onboarding, and knowledge management.

## Executive Summary

LeanKernel already owns the runtime, context-gating, routing, quality, retrieval, and knowledge foundations needed for a high-trust personal AI agent. Phase 4 packages those capabilities into a cohesive, operator-grade Blazor Server experience that makes the system usable, observable, and governable without shell access or raw file editing.

The UI must connect to the existing backend APIs rather than re-implement domain behavior in the browser. `LeanKernel.Host` remains the composition, API, and presentation layer; domain behavior continues to live in `LeanKernel.Core`, `LeanKernel.Thinker`, `LeanKernel.Archivist`, `LeanKernel.Commander`, `LeanKernel.Scheduler`, and `LeanKernel.Plugins`.

Phase 4 delivers five user-facing surfaces:

1. **Blazor Chat Interface** for daily interaction with the agent.
2. **Diagnostics Explorer** for turn-level auditability and explainability.
3. **Admin Console** for runtime governance and routing operations.
4. **Onboarding Wizard** for guided identity and preference setup.
5. **Knowledge Browser** for managing the GBrain wiki and relationship graph.

Success for Phase 4 means LeanKernel users can complete onboarding, run sessions, inspect why the agent behaved a certain way, manage runtime policy safely, and curate durable knowledge entirely from the web UI.

## Problem Statement

Phases 1–3 establish a backend-capable platform, but backend completeness alone is insufficient for a personal AI agent product. Users and operators still need a trustworthy interaction layer that exposes session continuity, streaming, diagnostics, governance, identity grounding, and knowledge management in an approachable way.

Without a complete UI layer:

- day-to-day chat remains too primitive for sustained use;
- context and routing decisions remain difficult to inspect and debug;
- runtime configuration depends too heavily on file editing and expert knowledge;
- onboarding is functional but not identity-rich or progressive;
- the GBrain wiki remains powerful but under-exposed as a user-facing capability.

Phase 4 closes that gap by shipping the full Blazor Server product surface on top of the existing API backend.

## Phase Dependency Statement

Phase 4 is explicitly downstream of Phases 1–3 and must not start as a substitute for unfinished backend work.

### Required prerequisite state

- **Phase 1: Core Runtime** is complete, tested, and stable:
  - native MAF agent runtime
  - sessions
  - tool exposure
  - deterministic context coordination baseline
  - instruction manifesting
  - static invocation path
- **Phase 2: Context and Personalization** is complete, tested, and stable:
  - identity grounding
  - onboarding gap detection
  - scoped retrieval
  - deterministic history shaping
  - context diagnostics
- **Phase 3: Reliability and Optimization** is complete, tested, and stable:
  - model routing
  - shadow routing
  - quality gates
  - escalation policies
  - workflow orchestration
  - response enhancement
  - post-turn learning

### Dependency rule

If a Phase 4 surface requires a backend contract that is not already implemented or stable, the gap must be treated as a dependency defect and resolved in the owning backend phase area rather than patched with UI-only logic.

## Goals

- Deliver a production-grade Blazor Server UI for the full LeanKernel product experience.
- Expose chat, diagnostics, governance, onboarding, and knowledge workflows from one coherent navigation model.
- Preserve LeanKernel's differentiators: deny-by-default context gating, deterministic history shaping, routing explainability, quality gates, and durable knowledge.
- Make system behavior inspectable enough that operators can answer "what happened and why" for any turn.
- Allow advanced administration without forcing operators to edit JSON, YAML, or markdown files directly.
- Make the product usable on desktop and mobile without requiring a separate mobile app.

## Non-Goals (v1)

- Replacing the existing backend APIs with UI-owned service logic.
- Moving core domain behavior from feature projects into `LeanKernel.Host`.
- Building a WebAssembly or JavaScript SPA alternative to Blazor Server.
- Designing a public multi-tenant collaboration product.
- Introducing a native mobile application.
- Replacing file-backed runtime configuration, wiki storage, or existing diagnostics persistence in this phase.
- Shipping a pixel-perfect analytics suite beyond the diagnostics and spend views needed for operator trust.

## Primary Users

- **Primary user:** a builder or technical operator using LeanKernel as a personal AI agent.
- **Administrator:** the person configuring routing, tool policy, providers, and agent definitions.
- **Debugger/operator:** a maintainer investigating context, retrieval, routing, or quality behavior.
- **Knowledge curator:** a user managing durable GBrain wiki content and relationships.

## Product Principles

### 1. API-backed, not UI-owned
All meaningful reads and writes must flow through the existing backend APIs or stable application services. The Blazor UI is an interaction layer, not a second runtime.

### 2. Explainability is a product feature
Where LeanKernel makes a deterministic choice—context inclusion, history shaping, routing, fallback, or quality rejection—the UI must make that choice visible.

### 3. Progressive disclosure
The default UI should feel calm and usable for daily work, while advanced controls remain available for operators who need deeper inspection.

### 4. Mobile-responsive by default
Every core user path must remain usable on narrow screens, especially chat, onboarding progress, and key diagnostics summaries.

### 5. Accessibility is table stakes
Keyboard navigation, focus order, semantic labeling, color contrast, and screen-reader compatibility are required, not optional refinements.

## User Stories

- As a user, I can start a new conversation, resume a prior one, and see the assistant stream a response in real time.
- As a user, I can tell when conversation history has been compacted or summarized instead of shown verbatim.
- As an operator, I can inspect what context was considered, what was selected, and why.
- As an admin, I can manage model routing, tool governance, and worker definitions without hand-editing configuration files.
- As a first-time user, I can complete guided onboarding that explains what information is missing and why it matters.
- As a returning user, I can keep enriching my profile and knowledge graph over time.
- As a knowledge curator, I can browse, search, edit, and link wiki pages from the UI.

## Functional Requirements

### FR-1 Blazor Chat Interface

The system must provide a full-featured Blazor Server chat workspace connected to the existing chat and session backend.

#### Requirements

- Provide an interactive Blazor Server chat experience at the core of the UI.
- Support session lifecycle actions:
  - create new session
  - list sessions
  - resume session
  - rename or label session when supported by backend contract
  - surface queued/quiet-hours state when applicable
- Display user and assistant messages as an ordered turn timeline.
- Support real-time streaming of assistant responses as partial tokens or partial message chunks arrive.
- Preserve and render prior turn history when resuming a session.
- Show visible indicators for history shaping states:
  - verbatim
  - compacted
  - summarized
- Support attachment upload from the chat composer using the existing attachment ingestion path.
- Show attachment state clearly:
  - uploaded
  - processing
  - accepted
  - rejected
  - unsupported type
- Support keyboard-first interaction:
  - Enter to send
  - Shift+Enter for newline
  - focus return to composer after send
- Provide stable error handling for send failures, queued responses, rate-limit style failures, and backend unavailability.
- Provide mobile-responsive layout behavior for session picker, message list, composer, and diagnostics affordances.

#### UI Requirements

| Region | Purpose |
| --- | --- |
| Session rail / drawer | Create, resume, search, and switch sessions |
| Message timeline | Render turns, timestamps, compacted indicators, attachments, and stream state |
| Composer | Text entry, attachment picker, send action, urgent toggle if supported |
| Turn metadata affordance | Quick access to diagnostics summary for each assistant turn |
| Empty state | Explain what LeanKernel can do and how to begin |

#### Data and contract requirements

- Consume session listing and session-history contracts from the chat backend.
- Consume chat send APIs rather than calling `IThinkerService` directly from UI components.
- Support streaming transport compatible with the API backend selected for Phase 4 implementation, such as SignalR or server-sent events.
- Preserve server-authoritative timestamps, session identifiers, and turn metadata.

#### Acceptance criteria

- A user can create a new session, send a message, and receive a streamed response without page reload.
- A user can resume an existing session and see the full server history in correct order.
- Compacted or summarized turns are visually distinguishable from verbatim turns.
- A supported attachment can be uploaded and included in a chat request from the UI.
- The chat experience remains usable on a mobile-width viewport.

### FR-2 Diagnostics Explorer

The system must provide a dedicated diagnostics surface for auditing how each turn was assembled, routed, and evaluated.

#### Requirements

- Provide a per-turn context audit view that shows:
  - system instruction inputs
  - identity/profile context inputs
  - history inputs
  - retrieval inputs
  - exclusions and reasons
- Visualize budget usage by category, including token usage or estimated token allocation for:
  - system prompt
  - history
  - wiki facts
  - retrieval knowledge
  - tool metadata
  - response headroom
- Show a history-shaping timeline that distinguishes:
  - verbatim turns
  - compacted turns
  - summarized turns
  - excluded turns
- Provide retrieval diagnostics showing candidates that were:
  - scored
  - selected
  - excluded
  - deprioritized
- Show routing decisions for a turn, including:
  - selected model alias
  - selected tier
  - reason for selection
  - fallback or escalation path
  - provider health state snapshot when available
  - alternatives considered
- Visualize quality gate outcomes, including pass/fail state and rejection reason when a response required escalation or retry.
- Support drill-down from a chat turn into its corresponding diagnostics record.
- Support timeline filtering by session, turn, component, and failure state.

#### UI Requirements

| Pane | Purpose |
| --- | --- |
| Turn navigator | Move across session turns and select a specific audit record |
| Context audit panel | Show included context blocks, exclusions, and token budget slices |
| Retrieval diagnostics panel | Show candidate lists, scores, selection reason, and exclusion reason |
| Routing panel | Show model decision, fallback path, and provider health status |
| Quality panel | Show gate outcomes, retry/escalation reason, and final disposition |

#### Data and contract requirements

- Consume diagnostics data emitted by the runtime, context gatekeeper, routing pipeline, and quality gate services.
- Backend diagnostics must expose stable, queryable DTOs rather than UI-specific string scraping.
- Selection log data, provider cooldown state, and quality-gate metadata must be retrievable per turn.

#### Acceptance criteria

- An operator can open a turn and inspect the context blocks selected for that turn.
- An operator can see per-category budget usage in a visual format.
- An operator can determine which retrieval candidates were selected versus excluded and why.
- An operator can identify which model was chosen, why it was chosen, and whether fallback or escalation occurred.
- An operator can see whether a quality gate passed, failed, or triggered an escalation path.

### FR-3 Admin Console

The system must provide an administrative workspace for governing routing, tools, agents, spend, and provider health.

#### Requirements

- Provide model routing configuration UI covering:
  - model tiers
  - alias selection
  - cost policies
  - fallback ladders
  - escalation rules
  - spend guard thresholds
- Provide tool governance management for:
  - enable/disable state
  - scope restrictions
  - policy editing
  - approval or deny-by-default posture where configured
- Provide agent definition management for:
  - workers
  - scopes
  - system prompts
  - capability or tool boundaries
- Provide spend tracking dashboards for:
  - per-session spend
  - daily spend
  - monthly spend
  - free versus paid usage mix
  - fallback/spend-guard events
- Provide provider health visibility using LiteLLM and routing health signals:
  - model availability
  - degraded providers
  - cooldown state
  - last failure signal when available
- Protect all administrative write actions behind the existing admin authentication model.
- Provide validation and diff review before persisting high-risk configuration changes.

#### UI Requirements

| Workspace | Purpose |
| --- | --- |
| Routing editor | Edit tiers, aliases, fallback order, escalation, and cost policy |
| Tool governance panel | Manage tool eligibility and policy text/metadata |
| Agent definitions panel | Edit worker definitions, prompts, and scope rules |
| Spend dashboard | Show trend cards, charts, and drill-down by session/time window |
| Provider health board | Show live availability and cooldown state for routed aliases |

#### Data and contract requirements

- Build on the existing configuration and routing APIs.
- Reuse runtime configuration metadata such as env-backed, restart-required, mutable, and secret masking flags.
- Persist through backend-owned config stores and controllers only.
- Expose spend and provider-health data through API contracts rather than UI polling against logs.

#### Acceptance criteria

- An admin can review and change routing policy from the UI without hand-editing source files.
- An admin can enable or disable tools and edit associated governance policy through the UI.
- An admin can manage worker/agent definitions and save valid changes through approved contracts.
- An admin can inspect spend by session, day, and month from the dashboard.
- An admin can identify whether a routed model alias is healthy, degraded, or unavailable.

### FR-4 Onboarding Wizard

The system must provide a guided onboarding flow that establishes durable user identity, preferences, and profile context over time.

#### Requirements

- Provide a step-by-step onboarding wizard rather than a flat form experience.
- Capture identity and preference information relevant to LeanKernel personalization, including:
  - preferred communication style
  - interests
  - domains of work
  - goals
  - operating constraints when relevant
- Create or update a GBrain wiki page representing the user's profile.
- Surface gap detection feedback that explains:
  - what information is missing
  - why it matters to answer quality
  - how the user can add it later
- Support progressive enhancement so onboarding can be completed minimally and expanded over time.
- Preserve progress and allow re-entry after initial completion.
- Show a completion summary with what was captured, what was skipped, and recommended next enrichment steps.
- Ensure onboarding content aligns with Archivist identity and onboarding-gap logic rather than inventing a second profile model in the UI.

#### UI Requirements

| Step | Purpose |
| --- | --- |
| Welcome and framing | Explain why identity grounding improves quality and reduces repetitive prompting |
| Identity basics | Capture name/preferred reference and primary role/context |
| Communication preferences | Capture tone, verbosity, format preferences, and working style |
| Interests and domains | Capture focus areas, recurring topics, and project domains |
| Gap review | Explain missing context and its expected impact |
| Completion summary | Confirm wiki/profile creation and suggest next actions |

#### Data and contract requirements

- Persist onboarding state through backend onboarding APIs.
- Trigger GBrain profile page creation or update through backend-owned wiki/profile services.
- Reuse backend gap-detection logic so UI feedback matches runtime behavior.

#### Acceptance criteria

- A new user can complete a guided identity and preferences flow from the UI.
- Completing onboarding creates or updates the expected GBrain user profile page.
- The wizard explains at least one meaningful profile gap and why it affects agent quality.
- A returning user can re-open onboarding and add more context later without losing prior answers.
- The onboarding experience works on desktop and mobile layouts.

### FR-5 Knowledge Browser

The system must provide a full UI for browsing, editing, searching, and visualizing the GBrain knowledge base.

#### Requirements

- Provide a wiki page viewer/editor for GBrain entries.
- Support create, edit, and delete workflows from the UI.
- Provide a search experience across the knowledge base by subject, facts, and relation-linked context.
- Provide graph visualization of entity relationships and page links.
- Provide a page history and timeline viewer showing how an entry changed over time when history data exists.
- Show page metadata such as:
  - dimension
  - confidence hints
  - last accessed
  - source citations
  - relations
- Support navigation between related pages from graph, relation chips, and inline links.
- Distinguish wiki content edits from derived/indexed search representations.
- Guard destructive actions with confirmation and role checks.

#### UI Requirements

| Region | Purpose |
| --- | --- |
| Search and filters | Query by text, dimension, relation, and recency |
| Entry list | Browse matching pages with metadata previews |
| Page editor/viewer | Read and edit page content and structured metadata |
| Graph panel | Visualize relationships between entities and linked pages |
| History timeline | Show revisions, notable changes, and access activity |

#### Data and contract requirements

- Consume wiki list/search/get/create/update/delete APIs owned by the backend.
- Graph and history views require explicit backend contracts; the UI must not derive historical truth by diffing rendered HTML.
- Search results must remain consistent with Archivist knowledge structures and tagging rules.

#### Acceptance criteria

- A user can search for and open a wiki page from the UI.
- A user can create, edit, and delete a page through backend-backed workflows.
- A user can visualize page relationships in a graph-oriented view.
- A user can inspect page history and timeline information when available.
- Navigating between related entities does not require leaving the knowledge browser context.

## Cross-Feature Acceptance Criteria

- AC-1: The Phase 4 UI ships as Blazor Server pages/components within `LeanKernel.Host` and uses the existing authenticated backend.
- AC-2: The chat experience supports new session creation, session resume, real-time response streaming, and attachment upload.
- AC-3: Compaction and summarization state are visible in both chat history and diagnostics views.
- AC-4: Every assistant turn can be traced to a diagnostics record containing context, retrieval, routing, and quality information.
- AC-5: The admin console can manage routing, tool governance, agent definitions, spend visibility, and provider health without shell access.
- AC-6: The onboarding wizard creates or updates durable user profile knowledge and supports progressive enrichment.
- AC-7: The knowledge browser supports search plus create, read, update, and delete workflows for wiki pages.
- AC-8: All high-risk administrative writes are authenticated, validated, and reviewable before persistence.
- AC-9: All core user journeys remain usable on mobile-width screens.
- AC-10: All core user journeys satisfy agreed accessibility requirements.

## Non-Functional Requirements

### Performance

- Chat page first interactive render: **<= 2.5s p95** on a warm local deployment.
- Session switch and history load: **<= 1.5s p95** for typical session sizes.
- Initial stream feedback after send: **<= 500ms p95** once the backend begins returning streamed output.
- Diagnostics turn drill-down: **<= 1.5s p95** for the most recent 100 turns in a session.
- Knowledge search result rendering: **<= 1.5s p95** for common queries under expected local dataset sizes.
- Admin dashboard refresh for spend/provider health summaries: **<= 2.0s p95**.

### Accessibility

- Meet **WCAG 2.1 AA** for all Phase 4 surfaces.
- All major workflows must be keyboard-accessible without pointer-only interactions.
- All form inputs, graph affordances, tabs, drawers, charts, and buttons must have semantic labels.
- Focus states must remain visible and consistent.
- Color cannot be the only signal for status such as success, failure, warning, compaction, or selection.
- Charts and graph views must provide text alternatives or summary tables.

### Responsiveness

- Support desktop, tablet, and mobile breakpoints without blocking core tasks.
- Chat composer, onboarding actions, and admin save controls must remain reachable on narrow screens.
- Data-dense diagnostics and admin views may collapse into stacked cards or drawers, but must preserve full functionality.
- Graph and chart experiences must degrade gracefully on mobile rather than becoming unreadable.

### Reliability

- UI failures must not corrupt backend state or silently lose administrative changes.
- Long-running UI actions must show loading, success, failure, and retry states.
- Streaming interruptions must fail gracefully with a recoverable user message.
- Draft or unsaved changes in admin and onboarding flows must be protected from accidental navigation loss.

### Security and Privacy

- All admin and knowledge-management write operations require authenticated authorization through the existing auth model.
- Secrets remain masked or reference-only in UI displays.
- UI telemetry and diagnostics must not expose raw credentials or hidden secret values.
- Attachment handling must respect existing backend validation and MIME/extension policy.

## API and Backend Expectations

Phase 4 assumes the API backend is already complete enough to support these surfaces. Where a stable contract does not yet exist, the backend must expose one before the UI depends on it.

### Required backend capability areas

- chat session list/get/send/stream contracts
- attachment ingestion contracts
- onboarding draft, validation, completion, and profile update contracts
- diagnostics contracts for context audit, retrieval audit, routing decisions, and quality gates
- config and routing management contracts
- tool governance and agent definition contracts
- spend and provider health summary contracts
- wiki search/get/create/update/delete/history/graph contracts

### UI architecture constraints

- Prefer server-driven Blazor components over heavy client-side state duplication.
- Use strongly typed DTOs for API interaction.
- Keep business rules in backend services, not Razor component code-behind logic.
- Reuse existing route structure where practical, including `/chat`, `/onboarding`, `/wiki`, and admin surfaces.

## Dependencies

### Product dependencies

- Phase 1 runtime/session/tooling capabilities
- Phase 2 context, history shaping, diagnostics, and identity capabilities
- Phase 3 routing, quality gates, escalation, and post-turn learning capabilities

### Technical dependencies

- `LeanKernel.Host` Blazor Server hosting model
- existing auth and admin policy enforcement
- chat/session APIs in `ChatController`
- config/routing APIs in `ConfigController`, routing config endpoints, and related services
- onboarding APIs and orchestrator services
- wiki APIs and wiki storage contracts
- diagnostics and routing metadata from `LeanKernel.Thinker` and `LeanKernel.Archivist`
- LiteLLM provider and alias health visibility
- Qdrant-backed knowledge search and wiki relationships

### Operational dependencies

- stable runtime configuration loading
- stable session persistence
- stable wiki persistence and indexing behavior
- stable diagnostics collection with retention sufficient for UI inspection

## Risks and Mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Backend diagnostics contracts are incomplete or inconsistent | Diagnostics Explorer becomes shallow or misleading | Define typed diagnostics DTOs before UI implementation and keep ownership in backend services |
| UI scope creep across five large surfaces delays delivery | Phase 4 slips and polish suffers everywhere | Ship by workflow priority: chat, diagnostics, admin, onboarding, knowledge; use shared layout/components |
| Too much logic moves into `LeanKernel.Host` | Architecture drift and duplicated behavior | Enforce API-backed interactions and keep domain logic in owning projects |
| Mobile support is treated as a late pass | Core workflows become desktop-only | Design responsive patterns from the first component library pass |
| Graph and chart views become inaccessible | Diagnostics and knowledge tooling exclude some users | Provide summary tables, keyboard alternatives, and screen-reader descriptions |
| Spend and provider health data are too coarse | Admin console cannot support trustable decisions | Require backend summary and drill-down contracts before finalizing admin dashboards |
| Attachment and streaming UX is unreliable | Users lose trust in the primary chat workflow | Add explicit state indicators, retries, and resilient transport handling |

## Success Metrics

### Adoption and engagement

- **>= 80%** of active LeanKernel users complete at least one chat session entirely through the Phase 4 UI.
- **>= 70%** of first-time users complete onboarding without leaving the wizard.
- **>= 50%** of onboarded users return to enrich profile context after initial completion.

### Chat workflow quality

- **>= 95%** of successful chat sends render first visible stream feedback within the target latency budget.
- **>= 90%** of session resumes load prior history without manual refresh or mismatch.
- Attachment upload success rate for supported file types: **>= 95%**.

### Diagnostics and trust

- **>= 90%** of inspected turns expose a complete diagnostics record with context, retrieval, routing, and quality sections populated.
- Mean time to understand a routing or retrieval issue is reduced relative to pre-Phase-4 operator workflow.
- Operator satisfaction with explainability improves in internal review or dogfooding surveys.

### Administration and governance

- **>= 90%** of admin configuration changes are completed through the UI without fallback to direct file editing.
- Provider health and spend dashboards reconcile with backend telemetry within agreed tolerances.
- Configuration save error rate remains below **2%** after validation and diff review are introduced.

### Knowledge and personalization

- **>= 80%** of users can locate a known wiki page within three interactions.
- Knowledge edit success rate remains above **95%** for non-conflicting edits.
- Onboarding-generated profile pages are present for **>= 90%** of completed onboarding flows.

## Rollout Plan

1. **Milestone 1: Chat foundation**
   - session list/resume/new
   - streaming response UX
   - attachment upload
   - compacted history indicators
2. **Milestone 2: Diagnostics Explorer**
   - turn drill-down
   - budget and history shaping visuals
   - retrieval/routing/quality panels
3. **Milestone 3: Admin Console**
   - routing, tool governance, agent definitions
   - spend and provider health dashboards
4. **Milestone 4: Onboarding Wizard refresh**
   - identity-rich guided flow
   - GBrain profile creation
   - progressive enrichment and gap review
5. **Milestone 5: Knowledge Browser expansion**
   - CRUD editor
   - graph view
   - history/timeline view

## Implementation Clarifications

- Phase 4 is a UI phase, not a backend-rearchitecture phase.
- Blazor Server is the required UI technology for all new Phase 4 surfaces.
- Existing partial pages such as `/chat`, `/onboarding`, `/wiki`, `/settings`, and routing/admin pages should be treated as seeds to evolve, not proof that the phase is already complete.
- Any UI feature that needs richer backend data must request that data through typed API expansion owned by the relevant backend feature area.
- Message streaming, diagnostics drill-down, and admin validation flows should be implemented with reusable components so Phase 4 feels like one product, not five disconnected tools.

## Sprint-Ready Engineering Tickets

- [ ] `UI4-01` Define shared Blazor layout, navigation, and responsive component primitives for Phase 4 surfaces.
- [ ] `UI4-02` Implement API-backed chat session rail, streaming composer, and attachment workflow.
- [ ] `UI4-03` Add history-shaping indicators and per-turn entry points into diagnostics.
- [ ] `UI4-04` Define diagnostics DTOs for context audit, budget slices, retrieval decisions, routing decisions, and quality gates.
- [ ] `UI4-05` Build Diagnostics Explorer turn timeline, charts, and drill-down views.
- [ ] `UI4-06` Expand admin APIs and UI for routing policy, tool governance, and agent definition management.
- [ ] `UI4-07` Implement spend tracking and provider health dashboards with drill-down filters.
- [ ] `UI4-08` Replace the current onboarding experience with an identity-first guided wizard and profile-gap review.
- [ ] `UI4-09` Add GBrain profile page creation/update integration to onboarding completion.
- [ ] `UI4-10` Expand wiki UI into a full knowledge browser with CRUD, graph, and page history views.
- [ ] `UI4-11` Validate WCAG 2.1 AA, keyboard navigation, and mobile responsiveness across all Phase 4 surfaces.
- [ ] `UI4-12` Add end-to-end tests for core chat, diagnostics, onboarding, admin, and wiki flows.

## Open Questions

- Which streaming transport will be the standard for Blazor chat: SignalR, server-sent events, or another backend-supported option?
- What is the authoritative persistence model for wiki page history and graph edges if the current backend does not yet expose them?
- Should spend tracking remain request-count based in the first UI release, or must it expose estimated currency cost immediately?
- How much diagnostics retention is required for daily-use troubleshooting in local-first deployments?
- Which admin actions require confirmation-only versus diff-review plus secondary safeguards?
