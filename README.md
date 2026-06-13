# LeanKernel — Lean Personal AI Agent

![LeanKernel logo](docs/assets/brand/logo.png)

LeanKernel is the personal AI agent for builders who want **reliable output, lower token spend, and full control of context**. Instead of bloated chat history and unpredictable behavior, LeanKernel gives you a lean, observable agent runtime that helps you ship more with less friction.

## Ideal Audience (LeanKernel/Hermes-Style Users)

LeanKernel is built for power users who already rely on AI daily and want more control than typical hosted assistants:

- **Indie hackers and solo builders** running multiple projects who need dependable execution, not chat novelty.
- **Technical operators and developers** who care about auditability, composability, and predictable costs.
- **Privacy-conscious professionals** who want local data ownership and explicit control over what enters model context.
- **Small teams replacing assistant sprawl** (many disconnected tools) with one orchestrated, observable agent runtime.

## Value Proposition: From Agent Chaos to Reliable Throughput

LeanKernel helps you turn AI from "interesting demos" into repeatable output with lower spend, better context quality, and less operational friction.

### Before vs After

| Before (typical agent stack) | After (LeanKernel) |
|-----------------------------|----------------|
| Context bloat drives rising token costs and weaker answers | Context gatekeeper injects only high-value memory to improve relevance and reduce waste |
| Hard to debug agent failures across tools and model hops | Structured middleware logs and diagnostics make runs observable and fixable |
| Knowledge is fragmented across chat history, docs, and ad hoc notes | Unified 5W1H wiki + durable memory services keep facts queryable and reusable |
| Switching models/providers requires code churn and config drift | LiteLLM routing centralizes model strategy and allows provider changes with minimal disruption |

### Benefit Stack (Feature -> Tangible Outcome -> Emotional Win)

- **Deny-by-default context gating** -> fewer irrelevant tokens and tighter prompts -> more confidence that answers stay on target.
- **Deterministic history shaping** -> recent turns stay verbatim while older turns compact into traceable summaries -> lower context spend without losing critical decisions.
- **5W1H structured memory + durable retrieval** -> faster recall of past facts and decisions -> less repeated explaining and lower frustration.
- **MAF multi-agent orchestration** -> specialized workers handle complex requests predictably -> more done per day with less manual juggling.
- **Dockerized core stack (Gateway, LiteLLM, Postgres, GBrain, optional browser service, optional Signal)** -> reproducible local runtime with fewer moving parts -> lower ops anxiety and easier recovery.
- **Document folder import (`./data/documents`)** -> drop files anywhere under a local bind mount and have the engine queue them for GBrain-backed document ingestion while managed upload copies stay in a separate volume.
- **Thin Gateway API + shared channel routing** -> API and Signal messages reuse one runtime path -> faster integration and clearer transport behavior during the rearchitecture.

## Main Pain Points with Existing AI Agents (Online Signals)

Recurring issues in production agent deployments map directly to the problems LeanKernel is designed to solve:

- **Complexity creep**: teams overbuild multi-agent flows where simpler patterns would work, increasing latency and cost ([Microsoft guidance](https://learn.microsoft.com/en-us/azure/architecture/ai-ml/guide/ai-agent-design-patterns)). LeanKernel counters this with focused coordinator-worker orchestration so you get dependable execution without architecture bloat.
- **Compounding errors and loop risk**: autonomous systems can stall, bounce, or loop without strong iteration limits and checkpoints ([Anthropic](https://www.anthropic.com/engineering/building-effective-agents), [Microsoft guidance](https://learn.microsoft.com/en-us/azure/architecture/ai-ml/guide/ai-agent-design-patterns)). LeanKernel reduces this risk through explicit middleware boundaries and source-aware retrieval that keep runs on track.
- **Weak observability**: abstraction-heavy stacks make failures hard to trace and fix, reducing trust ([Anthropic](https://www.anthropic.com/engineering/building-effective-agents), [IBM](https://www.ibm.com/think/topics/ai-agents)). LeanKernel addresses this with structured diagnostics and tool-level traces so troubleshooting is faster and clearer.
- **Cost unpredictability**: orchestration multiplies model calls and token usage without tight budgeting and compaction ([Microsoft guidance](https://learn.microsoft.com/en-us/azure/architecture/ai-ml/guide/ai-agent-design-patterns)). LeanKernel's deny-by-default context gating and model routing strategy help keep spend predictable.
- **Governance and privacy concerns**: agents need stronger guardrails, least-privilege access, and auditable action trails ([IBM](https://www.ibm.com/think/topics/ai-agents), [PwC](https://www.pwc.com/us/en/tech-effect/ai-analytics/ai-predictions.html)). LeanKernel reinforces this with local data ownership patterns and auditable runtime behavior.

## Architecture

LeanKernel is organized around a single agent runtime and feature-owned subsystems:

| Component | Role |
|-----------|------|
| **Agent Runtime** | Canonical entry point for each turn via `IAgentRuntime`. |
| **Channels** | Channel adapters, per-channel auth, and inbound/outbound transport routing. |
| **Thinker** | Turn orchestration, prompt assembly, tool dispatch, model invocation strategies, and post-turn event publication. |
| **Archivist** | Sessions, identity/profile artifacts, engagement policy, 5W1H wiki, vector search, capability gaps, and deny-by-default context gating. |
| **Scheduler** | Cron jobs and proactive maintenance. |
| **Plugins** | Built-in tools, attachment extraction, and runtime skill loading. |
| **Gateway** | ASP.NET Core composition root for the Minimal API surface plus the Phase 4 Blazor Server chat and onboarding UI, health/chat/diagnostics endpoints, and runtime wiring. |

### Component Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                    Docker Compose Network                       │
│                                                                 │
│  ┌──────────────┐   ┌───────────────────────────────────────┐  │
│  │ PostgreSQL   │◄─►│       LeanKernel-engine (.NET 10)     │  │
│  │ + pgvector   │   │  Gateway/Channels → Agent Runtime     │  │
│  └──────────────┘   │       │                    │           │  │
│                     │       ▼                    ▼           │  │
│  ┌──────────────┐   │  Agents/Tools        Diagnostics       │  │
│  │  LiteLLM     │◄─►│       │                    │           │  │
│  │  (proxy)     │   │       ▼                    ▼           │  │
│  └──────────────┘   │   Knowledge         Persistence        │  │
│                     │       │                                │  │
│  ┌──────────────┐   │       ▼                                │  │
│  │   GBrain     │◄─►│   Wiki + Memory MCP                    │  │
│  │   (MCP)      │   │                                        │  │
│  └──────────────┘   └───────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

See [docs/architecture/architecture.md](docs/architecture/architecture.md) for the contributor-oriented architecture explanation and ownership rules.

### Context Gatekeeper (Core Differentiator)

Unlike typical agents that send full conversation history to the LLM, LeanKernel's Archivist starts from **nothing** and only injects LeanKernels that earn their place:

```
[Query] → Intent Classification → 5W1H Wiki + Vector Search → Competitive Ranking → Budget Fill → Minimal Prompt
```

**Token budget allocation:** System Prompt 15% · Wiki Facts 20% · History 40% · RAG LeanKernels 20% · Tools 5%

History shaping is configurable under `LeanKernel:History`. By default LeanKernel keeps the newest six turns verbatim, compacts the next ten turns through LiteLLM, summarizes the next twenty turns, and persists compaction markers for auditability.

**Scoring formula:** `(semantic_similarity × 0.40) + (recency_decay × 0.20) + (dimension_match × 0.25) + (interaction_frequency × 0.15)`

### Scoped Retrieval Policies

Phase 2 adds `LeanKernel:Retrieval` for deterministic scope enforcement, bounded entity expansion, and per-candidate retrieval diagnostics. Configure named scope policies in `src/LeanKernel.Gateway/appsettings.json` and optionally pass request metadata such as `retrieval_scope`, `task_scope`, or `agent_scope` to choose the effective policy without silently widening retrieval.

### Intelligent Model Routing

Phase 3 adds an optional routed execution path under `LeanKernel:Routing`. When enabled, LeanKernel scores task complexity, selects an economy/standard/premium model tier, and can escalate to a higher tier when deterministic quality gates fail. The same config block also carries deterministic response quality settings such as minimum output length, constraint coverage thresholds, and simple refusal-pattern matching. When `ShadowRoutingEnabled=true` and `ShadowModel` is configured, LeanKernel also invokes a non-authoritative shadow model in parallel, records both outputs for diagnostics, and still returns only the primary response. When disabled, the runtime keeps using the existing single-model `StaticAgentStrategy` path.

### Response Enhancement

Phase 3 also adds a synchronous response enhancement stage under `LeanKernel:Enhancement`. When enabled, LeanKernel runs deterministic post-model steps before delivery so it can append compact source notes, soften benign false refusals, and optionally inject inline citations. Enhancement is timeout-bounded, side-effect-free, and falls back to the original response if the enhancement pipeline fails or exceeds its budget.

### Post-Turn Learning

Phase 3 also adds an asynchronous post-turn learning stage under `LeanKernel:Learning`. Completed assistant turns can be published to a bounded background queue, then processed by ordered `ILearningStep` implementations for fact extraction, capability-gap tracking, and engagement metrics. Learning never blocks delivery: when the queue is full, LeanKernel drops the oldest queued learning event instead of backpressuring the user-facing turn path.

### Scheduled Jobs and Proactive Tasks

Phase 3 also adds a disabled-by-default scheduler under `LeanKernel:Scheduler`. The Gateway can evaluate Cronos-based cron jobs on a bounded background loop, run proactive `agent-prompt` turns through the same `IAgentRuntime` used for user messages, execute `knowledge-refresh` and `maintenance` jobs, and persist every execution to PostgreSQL for audit/history. Scheduler shutdown stops accepting new work and waits for in-flight jobs to finish.

### Coordinator-Worker Orchestration

Phase 3 also adds disabled-by-default coordinator-worker orchestration under `LeanKernel:Orchestration`. When enabled, LeanKernel can expose specialized workers as tools to a coordinator, scope each worker to specific tool names or categories, enforce per-worker timeout/depth/concurrency limits, and emit structured worker contribution traces for diagnostics and replay.

### Production Hardening

Phase 3 also adds an independently configurable hardening layer under `LeanKernel:Hardening`. The Gateway now supports correlation IDs, per-caller rate limiting, provider health tracking for PostgreSQL/LiteLLM/GBrain, node-local spend guardrails, graceful degradation when providers fail, fuller OpenTelemetry wiring, and Docker/container health checks that use `/api/health`.

### 5W1H Wiki System

Knowledge is stored as structured facts across six dimensions:

| Dimension | Content |
|-----------|---------|
| **Who** | People, organizations, entities |
| **What** | Events, actions, concepts |
| **Where** | Locations, environments |
| **When** | Temporal facts, schedules |
| **Why** | Reasons, motivations, causes |
| **How** | Processes, methods, procedures |

Each fact carries confidence scores, source citations, and is automatically extracted from conversations via heuristic pattern matching.

### Self-Improvement by Default

Every successful turn can emit a durable `TurnEvent` after the response is returned. A background learning worker drains a bounded queue through configured `ILearningStep` implementations for LiteLLM-backed fact extraction, deterministic capability-gap detection, and engagement metrics updates. This keeps the user-facing path fast while making post-turn learning explicit, observable, and bounded by configuration.

Wiki entries are stored as **markdown files** with YAML frontmatter — human-readable, editable, and git-friendly:

```markdown
---
id: who-alice-smith
dimension: who
subject: Alice Smith
lastAccessed: 2024-06-15T10:00:00Z
accessCount: 12
---

# Alice Smith

- Alice is a software engineer at Acme Corp <!--{confidence: 0.9, source: session-123, confirmed: 2024-06-01}-->
- Alice prefers TypeScript over JavaScript <!--{confidence: 0.85, source: session-456, confirmed: 2024-06-10}-->

## Related

- [Project Atlas](../what/project-atlas.md)
```

### Shared Persistence and Memory Services

The rearchitected local stack centers on a shared PostgreSQL instance for LeanKernel, LiteLLM, and the dedicated GBrain MCP service instead of separate Qdrant, Unstructured, and indexer sidecars.

| Service | Role |
|---------|------|
| **PostgreSQL + pgvector** | Primary persistence for LeanKernel, LiteLLM, and GBrain wiki/vector state |
| **GBrain** | Bun-hosted `garrytan/gbrain` wiki and memory MCP server, persisting to the shared Postgres service and serving HTTP MCP from the root endpoint |
| **LiteLLM** | Model proxy and routing surface for the engine |
| **Browser service** | Optional Webwright + Playwright sidecar for disabled-by-default browser automation tools |

The engine connects to LiteLLM through `LEANKERNEL__LITELLM__BASEURL`, to GBrain through `LEANKERNEL__GBRAIN__BASEURL`, to the optional browser sidecar through `LEANKERNEL__WEBWRIGHT__BASEURL`, and to PostgreSQL through `LEANKERNEL__DATABASE__CONNECTIONSTRING`.

## Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| Runtime | .NET 10 / C# 14 | 10.0 |
| Orchestration | Microsoft Agent Framework | 1.3.0 |
| AI Abstractions | Microsoft.Extensions.AI | 10.5.0 |
| OpenAI SDK | OpenAI | 2.10.0 |
| LLM Proxy | LiteLLM | `main-latest` |
| Persistence | PostgreSQL + pgvector | 16 |
| Wiki/Memory MCP | GBrain | `garrytan/gbrain` via Bun |
| Browser automation | Webwright + Playwright | `webwright==0.0.7` |
| Logging | Serilog | 9.0.0 |
| Containers | Docker Compose | v2 |

## Quick Start (First Value in Minutes)

Spin up LeanKernel, complete guided onboarding, and run your first production-style agent flow from a single local stack. Your first measurable win should be lower prompt bloat, clearer execution traces, and faster recovery when something fails.

```bash
# 1) Configure environment
cp .env.example .env
# Add LiteLLM backend provider keys in .env as needed (for example OPENAI_API_KEY, GROQ_API_KEY, GEMINI_API_KEY, AZURE_AI_API_KEY)
# GBrain uses LITELLM_BASE_URL and LITELLM_API_KEY from the same root env file.
# Keep GBRAIN_EMBEDDING_MODEL and GBRAIN_EMBEDDING_DIMENSIONS aligned with the GBrain pgvector schema.
# Browser automation is disabled by default; set WEBWRIGHT_ENABLED=true plus a random
# WEBWRIGHT_API_TOKEN and scoped WEBWRIGHT_LITELLM_KEY to enable browser tools.

# 2) Start the supporting services
# (Signal is optional and commented out by default in docker-compose.yml)
docker compose up -d

# 3) Verify health
curl http://localhost:5080/api/health

# 4) Run a chat turn (X-Api-Key only needed when configured)
curl http://localhost:5080/api/chat \
  -H "Content-Type: application/json" \
  -d '{"message":"Hello from LeanKernel","metadata":{"retrieval_scope":"personal"}}'

# 5) Open the Blazor workspace
# http://localhost:5080/          (chat)
# http://localhost:5080/onboarding (guided setup)
```

In Development, the generated OpenAPI document is available at `/openapi/v1.json`, and the Gateway also serves the interactive Blazor workspace from `/` plus the onboarding wizard at `/onboarding`.

## User Interface

- **Chat (`/`, `/chat`, `/chat/{sessionId}`):** The Chat page is the main Blazor workspace for day-to-day conversations. It supports creating and resuming sessions, renders persisted history with best-effort compaction badges, and shows a live loading state while the runtime completes a turn.
- **Diagnostics (`/diagnostics`):** The Diagnostics page loads persisted session diagnostics and turns them into context, budget, history, routing, and quality panels. It is the operator view for answering what the runtime admitted, excluded, selected, and evaluated for a given session.
- **Admin (`/admin`):** The Admin page is a mock-backed governance preview for provider health, routing tables, tool governance, spend summaries, and scheduled jobs. It validates the UI surface and interaction model, but the current implementation does not persist real admin changes.
- **Knowledge (`/knowledge`):** The Knowledge page is a wiki browser for GBrain-backed pages with debounced search, paged browsing, detail views, inline editing, and page creation. It can enrich page details and browsing with GBrain MCP tools when those capabilities are available.
- **Onboarding (`/onboarding`):** The Onboarding page is a five-step guided setup flow for identity, domains, and goals. It stores the resulting profile in GBrain wiki pages so personalization stays durable and inspectable.

### Local Development

```bash
# Requires .NET 10 SDK
cd src
dotnet build LeanKernel.sln
dotnet test LeanKernel.sln
dotnet run --project LeanKernel.Gateway
```

### Running Tests

```bash
cd src
dotnet test LeanKernel.sln -v minimal
```

### Quality Gates

```bash
# Coverage gate, default threshold: 80% line coverage
scripts/quality/test-coverage.sh

# Local Docker-backed SonarQube scan
scripts/quality/sonarqube-scan.sh
```

See [docs/development/quality.md](docs/development/quality.md) for details and environment variables.

## Project Structure

```
LeanKernel/
├── docker-compose.yml          # 4 core services plus optional commented Signal daemon
├── docker-compose.sonar.yml    # Standalone local SonarQube service
├── Dockerfile                  # Multi-stage .NET 10 build for LeanKernel.Gateway
├── .env.example                # Local infrastructure environment template
├── config/
│   ├── gbrain/
│   │   ├── Dockerfile          # Bun-based garrytan/gbrain image
│   │   └── install-gbrain.sh   # Local Bun installer helper
│   ├── litellm/
│   │   ├── config.yaml         # LiteLLM routing/config source file
│   │   └── render_litellm_config.py
│   └── signal/
│       ├── daemon.py           # Signal HTTP bridge contract used by SignalChannel
│       └── README.md           # Registration/daemon notes for optional Signal support
├── data/
│   └── wiki/                   # Optional local wiki content; Compose runtime state now persists in the gbrain-data volume
├── scripts/
│   ├── db/
│   │   └── init.sql            # Postgres extension bootstrap
│   └── quality/                # Coverage + Sonar validation helpers
└── src/
    ├── LeanKernel.Abstractions/     # Shared contracts, models, and configuration
    ├── LeanKernel.Agents/           # Agent runtime and turn orchestration
    ├── LeanKernel.Channels/         # Channel adapters, auth, routing, and hosted lifecycle
    ├── LeanKernel.Context/          # Context gating and prompt assembly
    ├── LeanKernel.Diagnostics/      # Logging, metrics, and diagnostics primitives
    ├── LeanKernel.Gateway/          # ASP.NET Core Minimal API composition root
    ├── LeanKernel.Knowledge/        # GBrain-backed knowledge access
    ├── LeanKernel.Persistence/      # Postgres-backed sessions and diagnostics
    ├── LeanKernel.Tools/            # Tool registry and execution
    ├── LeanKernel.Tests.Unit/       # Unit tests
    └── LeanKernel.Tests.Integration/ # Integration tests
```

## Configuration

All configuration is via `appsettings.json` or environment variables (using `__` separator for nested keys):

```bash
# Override via environment
LEANKERNEL__LITELLM__BASEURL=http://litellm:4000
LEANKERNEL__LITELLM__DEFAULTMODEL=gpt-4o-mini
LEANKERNEL__GBRAIN__BASEURL=http://gbrain:8789
LEANKERNEL__WEBWRIGHT__ENABLED=false
LEANKERNEL__WEBWRIGHT__BASEURL=http://webwright:8000
LEANKERNEL__IDENTITY__USERPREFERENCEPAGEKEY=identity-user-default
LEANKERNEL__IDENTITY__ENABLEIDENTITYEXTRACTION=true
LEANKERNEL__DATABASE__CONNECTIONSTRING=Host=database;Database=leankernel;Username=leankernel;Password=leankernel-dev-password
LEANKERNEL__GATEWAY__APIKEY=replace-me
```

### Identity and onboarding

`LeanKernel:Identity` configures the GBrain-backed agent profile page, user preference page, onboarding threshold/question limits, and whether post-turn identity extraction writes allowlisted preference updates back to GBrain.

### Gateway API key

The current `LeanKernel.Gateway` slice uses a simple API-key check for `POST /api/chat` and the diagnostics routes under `/api/diagnostics/*`.

```bash
# Leave empty for local development without auth
LEANKERNEL__GATEWAY__APIKEY=

# Or require a key on chat + diagnostics requests
LEANKERNEL__GATEWAY__APIKEY=replace-me
```

When a key is configured, send it with `X-Api-Key`:

```bash
curl http://localhost:5080/api/chat \
  -H "X-Api-Key: replace-me" \
  -H "Content-Type: application/json" \
  -d '{"message": "Hello from LeanKernel"}'
```

See [docs/features/authentication.md](docs/features/authentication.md) for the full authentication PRD.

### LiteLLM Configuration

`config/litellm/config.yaml` remains the local LiteLLM source spec. The rearchitected Compose stack mounts `./config/litellm` into the LiteLLM container, renders the runtime config with `render_litellm_config.py`, and then starts LiteLLM with the generated file.

At minimum, configure the environment expected by the local stack:

```bash
LITELLM_MASTER_KEY=sk-leankernel-local
DEFAULT_MODEL=gpt-4o-mini
CONTEXT_WINDOW_TOKENS=128000
OLLAMA_BASE_URL=http://host.docker.internal:11434
# Or provide hosted-provider keys such as OPENAI_API_KEY
```

You can keep extending `config/litellm/config.yaml` with additional providers, aliases, and routing policies as needed.

### Browser service configuration

Browser automation is exposed through four optional tools: `browser_run_task`, `browser_get_run`, `browser_get_artifact`, and `browser_cancel_run`. The tools register only when `LeanKernel:Webwright:Enabled=true`, and Webwright model calls route through LiteLLM using the sidecar's `WEBWRIGHT_LITELLM_KEY`.

Keep `WEBWRIGHT_API_TOKEN` blank until you intentionally enable the sidecar, then set it to a random secret and pass the same value to `LeanKernel:Webwright:ApiToken`.

## Multi-Agent System

LeanKernel uses the **MAF Agent-as-Tool** pattern — specialized worker agents are exposed as `AIFunction` tools on a coordinator agent. The LLM natively decides which specialists to invoke:

- **Simple queries** → direct `ChatClientAgent.RunAsync()` (no overhead)
- **Complex queries** → coordinator agent delegates to worker tools:
  - **ResearchWorker** — web search + summarization (4K token budget)
  - **CodeWorker** — code generation (8K token budget)
  - **ScheduleWorker** — calendar/reminder management (2K token budget)

### MAF Middleware Pipeline

| Layer | Middleware | Purpose |
|-------|-----------|---------|
| IChatClient | `ContextGatingMiddleware` | Prunes messages to token budget before LLM call |
| IChatClient | `FunctionLoggingMiddleware` | Logs tool invocations and results |
| Agent Run | `DiagnosticsMiddleware` | Timing, token counts, tool call stats → StateBag |

## Gateway API

`LeanKernel.Gateway` currently exposes the thin HTTP surface over the composed runtime, while `LeanKernel.Channels` adds optional Phase 2 inbound channel routing through the same runtime entry point.

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/api/health` | GET | Anonymous | Health check plus core DI service status. |
| `/api/chat` | POST | `X-Api-Key` when configured | Run one turn through `IAgentRuntime` and return the response with a session id. |
| `/api/diagnostics/{sessionId}` | GET | `X-Api-Key` when configured | Retrieve persisted diagnostics for a session. |
| `/api/diagnostics/{sessionId}/context` | GET | `X-Api-Key` when configured | Return the latest or requested per-turn context admission audit. |
| `/api/diagnostics/{sessionId}/budget` | GET | `X-Api-Key` when configured | Return per-category context budget allocation and usage for a turn. |
| `/api/diagnostics/{sessionId}/history` | GET | `X-Api-Key` when configured | Return persisted history shaping diagnostics for a turn. |

### OpenAPI

In Development, Gateway exposes its generated OpenAPI document at `/openapi/v1.json`.

### Optional channel ingress

Phase 2 adds a shared channel abstraction plus a disabled-by-default Signal adapter. Channel messages are authenticated per channel, normalized, then routed through the same `IAgentRuntime` entry point used by `/api/chat`.

### Planned UI and compatibility endpoints

The rearchitecture plans additional UI, auth, and OpenAI-compatible surfaces, but they are not implemented in the current `LeanKernel.Gateway` slice yet.

## Plugin System

Tools are registered in `AddLeanKernelTools` as `ToolDefinition` entries with scoped handlers. Add a tool by creating a built-in tool class and registering its `Create(...)` definition in the tools service collection extension.

Built-in tools include:
- `knowledge`: `wiki_search`, `wiki_read`, `wiki_write`
- `internet`: `web_search`, `web_fetch`, `http_request`
- `filesystem`: directory/file operations plus `extract_text`
- `data`: `json_transform`, `csv_xlsx_read_write`, `database_query`
- `browser`: `browser_run_task`, `browser_get_run`, `browser_get_artifact`, `browser_cancel_run` when `LeanKernel:Webwright:Enabled=true`

## Backup & Restore

```bash
# Backup wiki
./scripts/wiki-backup.sh backup

# List backups
./scripts/wiki-backup.sh list

# Restore from backup
./scripts/wiki-backup.sh restore data/backups/LeanKernel-wiki-20260429.tar.gz
```

## Local Service Ports

| Service | Default port | Purpose |
|---------|--------------|---------|
| LeanKernel Engine | `5080` | Gateway HTTP API, chat endpoint, and health check |
| LiteLLM | `4000` | Model proxy |
| PostgreSQL + pgvector | `5432` | Shared persistence for the stack |
| GBrain | `8789` | Wiki and memory MCP service |
| Browser service | `8000` | Optional Webwright/Playwright sidecar |

## Logging

LeanKernel uses Serilog with structured logging:

- **Console**: real-time output with timestamp, level, source context
- **File**: rolling daily logs in `data/logs/LeanKernel-*.log` (14 day retention, 10MB per file)

## Design Decisions

| Decision | Rationale |
|----------|-----------|
| Deny-by-default context gating | Token efficiency — never send what isn't needed |
| Source generators for tool registry | Zero-reflection, lean binary, AOT-ready |
| 5W1H structured wiki | Semantic organization enables precise retrieval |
| Tiered conversation aging | Turns 0-3 full, 4-8 summarized, 9-15 one-line, 16+ archived to wiki |
| LiteLLM as proxy | Model-agnostic with hot-reload, no code changes for new providers |
| Docker Compose isolation | Security boundaries, reproducible deployment |

## Feature Enhancements to Make LeanKernel the Top Choice

The following enhancements are prioritized to overcome common buyer objections and strengthen LeanKernel's differentiation.

| Objection | Enhancement | Customer Benefit |
|-----------|-------------|------------------|
| "I don't trust autonomous agents in production." | **Policy-based autonomy levels** (suggest-only, approve-before-action, auto-execute by tool/category) | Safer rollout path from pilot to production with explicit control at each step |
| "Agent behavior is hard to understand." | **Run replay + decision timeline UI** (prompt slices, tool calls, context sources, token/cost per step) | Faster debugging and stronger team trust through transparent reasoning traces |
| "Costs can spike without warning." | **Hard budget guardrails** (per-session, per-agent, per-day limits + auto model downgrades) | Predictable spend and fewer billing surprises |
| "Memory quality degrades over time." | **Memory hygiene jobs** (staleness scoring, contradiction detection, merge/summarize candidates) | Higher retrieval accuracy and less context pollution |
| "Setup still feels technical." | **Guided deployment profiles** (local dev, homelab, small-team cloud) with one-click validation | Faster time-to-first-value for non-expert operators |
| "I need proof this improves outcomes." | **Outcome analytics dashboard** (resolution rate, human takeover rate, latency, cost per successful task) | Clear ROI narrative for adoption decisions |

### Proposed Near-Term Roadmap

1. Add autonomy policy engine and per-tool approval gates.
2. Ship run replay, cost timeline, and context provenance views in the web UI.
3. Implement budget enforcement with graceful fallback routing in LiteLLM profiles.
4. Add memory hygiene and quality scoring pipelines for wiki and session history.
5. Publish benchmark scenarios (support triage, research synthesis, coding tasks) with reproducible metrics.

## License

MIT License. See [LICENSE](LICENSE).
