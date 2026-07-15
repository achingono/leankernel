# Plans

Implementation plans for the LeanKernel rebuild. Each phase folder follows the
[planning templates](../templates/index.md): `index.md`, `inputs.md`, `activities.md`,
`outputs.md`, `exit-criteria.md`, `risk-register.md`, and `evidence.md`.

Per [AGENTS.md](../../AGENTS.md), draft a plan here, have it reviewed in a separate
session, then implement and verify.

## Phased Plans

Phases are ordered but can overlap where dependencies allow. Phases 03–09 were derived
from a gap analysis of the mature source implementation against the current rebuild
(Core / Data / Logic / Gateway).

| Phase | Plan | Focus | Status |
| --- | --- | --- | --- |
| 01 | [Tool Runtime Enablement](phase-01-built-in-tools/index.md) | Built-in + dynamic tool runtime behind `/v1/responses` | **Complete** |
| 02 | [Runtime Boundary Remediation](phase-02-runtime-boundary-remediation/index.md) | Project/runtime boundary cleanup | **Complete** |
| 03 | [Turn Runtime And Context Gating](phase-03-turn-runtime/index.md) | Turn pipeline, deny-by-default context + budgets, history shaping, scoped retrieval, long-running tasks | Not started |
| 04 | [Model Intelligence And Response Quality](phase-04-model-intelligence/index.md) | Model routing/escalation, shadow routing, quality gates, response enhancement, degradation, multi-agent |
| 05 | [Tool Expansion](phase-05-tool-expansion/index.md) | Filesystem suite, data tools, web_fetch/http, browser tool, document ingestion |
| 06 | [Channels](phase-06-channels/index.md) | Channel abstraction/router, Signal adapter, fail-closed auth, keep-alive, attachments |
| 07 | [Learning And Scheduler](phase-07-learning-scheduler/index.md) | Learning pipeline, onboarding intelligence, cron scheduler |
| 08 | [Diagnostics And Production Operations](phase-08-diagnostics-ops/index.md) | Diagnostics collector + API, health/spend guardrails, OpenTelemetry, gateway hardening |
| 09 | [Blazor User Interface](phase-09-ui/index.md) | Chat, Diagnostics, Admin, Knowledge, Onboarding UIs |

## Personal-Assistant Expansion

Highest-leverage features that extend the runtime into a full personal assistant. The lead
requirement is that a user's memory and context follow them across **all** channels; today
memory is channel-scoped, so Phase 10 is foundational for the rest.

| Phase | Plan | Focus |
| --- | --- | --- |
| 10 | [Unified Identity And Cross-Channel Memory](phase-10-cross-channel-memory/index.md) | Person identity, verified identity linking, person-scoped (cross-channel) memory + preferences |
| 11 | [Integration Connector Hub](phase-11-integration-hub/index.md) | OAuth connector framework + per-person encrypted credential vault |
| 12 | [Email And Calendar Assistant](phase-12-comms-assistant/index.md) | Email triage/draft/send, calendar availability/scheduling over connectors |
| 13 | [Task And Reminder Management](phase-13-task-reminders/index.md) | Cross-channel tasks, reminders, follow-ups, proactive nudges |
| 14 | [Autonomy And Approval Policy Engine](phase-14-autonomy-approvals/index.md) | Risk-gated action approvals + tamper-evident action audit log |
| 15 | [Channel Identity Mapping](phase-15-channel-identity-mapping/index.md) | Resolve channel-native identifiers (e.g., Signal phone numbers) to known user identities instead of anonymous guests |
| 16 | [Identity Claims To Agent Context](phase-16-identity-claims-context/index.md) | Persist OIDC/OAuth claims to the DB and inject an allowlisted identity block into the system prompt |

## Cost And Model Telemetry

Telemetry foundations that make budget/cost accounting accurate and produce labeled data for
self-improvement (model grouping, failover order, cost profiles). Consumed by Phases 04, 07, and 08.

| Phase | Plan | Focus |
| --- | --- | --- |
| 17 | [Model Telemetry In Chat History](phase-17-model-telemetry-chat-history/index.md) | Persist LiteLLM model/provider/token-usage/cost per assistant turn for budget accounting and supervised tuning |

## Standalone PRDs

| Plan | Focus |
| --- | --- |
| [Identity-Partitioned Agent Runtime](prd-agent-runtime-persistence.md) | Agent runtime on Microsoft Agent Framework |
| [Agent Runtime Persistence — Gap Analysis](prd-agent-runtime-persistence-analysis.md) | Gap analysis for the runtime persistence PRD |
| [Import 5W1H Memory Logic](prd-5w1h-memory-logic.md) | 5W1H memory pipeline into `LeanKernel.Logic` |
| [Browser Built-in Tool + Playwright Service](browser-built-in-tool-playwright-service-prd.md) | Browser automation tool (superseded/expanded by Phase 05) |
