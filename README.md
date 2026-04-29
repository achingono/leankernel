# 🔷 LeanKernel — Lean Personal AI Agent

A high-performance, cost-optimized personal AI agent built on **.NET 10 / C# 14**. LeanKernel prioritizes **token efficiency** through tight context management and a modular backend.

## Architecture

LeanKernel is organized into three decoupled subsystems:

| Component | Role |
|-----------|------|
| **Commander** | Channel adapters (Signal, future Telegram/Discord). Routes messages. |
| **Thinker** | LLM reasoning via Semantic Kernel. Prompt assembly, tool dispatch, agent orchestration. |
| **Archivist** | Memory & context gatekeeper. 5W1H wiki, vector search, deny-by-default context gating. |

### Component Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                    Docker Compose Network                        │
│                                                                  │
│  ┌────────────┐   ┌──────────────────────────────────────────┐  │
│  │ signal-cli  │◄─►│         LeanKernel.Engine (.NET 10)           │  │
│  │ (JSON-RPC)  │   │                                          │  │
│  └────────────┘   │  Commander ──► Thinker ──► Archivist      │  │
│                    │       │            │           │           │  │
│  ┌────────────┐   │       │            │           ▼           │  │
│  │  LiteLLM   │◄─►│       │            │      5W1H Wiki       │  │
│  │  (proxy)   │   │       │            │      Qdrant Search   │  │
│  └────────────┘   │       │            ▼                       │  │
│                    │       │     Agent Orchestrator             │  │
│  ┌────────────┐   │       │     ├── ResearchWorker             │  │
│  │  Qdrant    │◄─►│       │     ├── CodeWorker                 │  │
│  │  (vectors) │   │       │     └── ScheduleWorker             │  │
│  └────────────┘   │       ▼                                    │  │
│                    │  Scheduler · Plugins · Source Generators   │  │
│                    └──────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### Context Gatekeeper (Core Differentiator)

Unlike typical agents that send full conversation history to the LLM, LeanKernel's Archivist starts from **nothing** and only injects LeanKernels that earn their place:

```
[Query] → Intent Classification → 5W1H Wiki + Vector Search → Competitive Ranking → Budget Fill → Minimal Prompt
```

**Token budget allocation:** System Prompt 15% · Wiki Facts 20% · History 40% · RAG LeanKernels 20% · Tools 5%

**Scoring formula:** `(semantic_similarity × 0.40) + (recency_decay × 0.20) + (dimension_match × 0.25) + (interaction_frequency × 0.15)`

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

## Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| Runtime | .NET 10 / C# 14 | 10.0 |
| Orchestration | Microsoft Semantic Kernel | 1.74.0 |
| LLM Proxy | LiteLLM | v1.83.7-stable |
| Vector DB | Qdrant | v1.17.1 |
| Messaging | signal-cli | latest |
| Logging | Serilog | 9.0.0 |
| Containers | Docker Compose | v2 |

## Quick Start

```bash
# 1. Clone and configure
cp .env.example .env
# Edit .env with your API keys and Signal number

# 2. Start all services
docker compose up -d

# 3. Check health
curl http://localhost:5080/health
```

### Local Development

```bash
# Requires .NET 10 SDK
cd src
dotnet build LeanKernel.sln
dotnet test LeanKernel.sln
dotnet run --project LeanKernel.Host
```

### Running Tests

```bash
cd src
dotnet test LeanKernel.sln -v minimal
```

## Project Structure

```
LeanKernel/
├── docker-compose.yml          # 3 services: engine, litellm, qdrant
├── Dockerfile                  # Multi-stage .NET 10 build
├── .env.example                # Environment variables template
├── config/
│   └── litellm/config.yaml     # LLM model routing
├── data/
│   ├── wiki/                   # 5W1H knowledge filesystem
│   │   ├── who/ what/ where/ when/ why/ how/
│   │   └── .LeanKernel/             # Embedding cache, digest
│   ├── sessions/               # Conversation history
│   ├── qdrant/                 # Vector DB storage
│   └── logs/                   # Rolling application logs
├── scripts/
│   ├── setup-signal.sh         # Signal registration helper
│   └── wiki-backup.sh          # Wiki backup/restore
└── src/
    ├── LeanKernel.Core/             # Interfaces, models, configuration
    ├── LeanKernel.Commander/        # Channel adapters (Signal)
    ├── LeanKernel.Thinker/          # LLM reasoning + agent orchestration
    ├── LeanKernel.Archivist/        # Memory, context gatekeeper, wiki
    ├── LeanKernel.Scheduler/        # Cron-based proactive tasks
    ├── LeanKernel.Plugins/          # Tool/plugin system
    ├── LeanKernel.Generators/       # Roslyn source generators
    ├── LeanKernel.Host/             # Application entry point
    └── LeanKernel.Tests.Unit/       # Unit tests
```

## Configuration

All configuration is via `appsettings.json` or environment variables (using `__` separator for nested keys):

```bash
# Override via environment
LEANKERNEL__LiteLlm__BaseUrl=http://litellm:4000
LEANKERNEL__LiteLlm__DefaultModel=claude-sonnet-4-20250514
LEANKERNEL__Qdrant__Host=localhost
LEANKERNEL__Signal__Enabled=false
```

### LiteLLM Model Routing

Configure `config/litellm/config.yaml` to route to different providers:

```yaml
model_list:
  - model_name: gpt-4o-mini
    litellm_params:
      model: openai/gpt-4o-mini
  - model_name: claude-sonnet
    litellm_params:
      model: anthropic/claude-sonnet-4-20250514
  - model_name: local-llama
    litellm_params:
      model: ollama/llama3
      api_base: http://host.docker.internal:11434
```

## Multi-Agent System

LeanKernel uses a lightweight orchestrator that analyzes query complexity:

- **Simple queries** → direct LLM invocation (no overhead)
- **Complex queries** → decomposed and delegated to specialized workers:
  - **ResearchWorker** — web search + summarization (4K token budget)
  - **CodeWorker** — code generation (8K token budget)
  - **ScheduleWorker** — calendar/reminder management (2K token budget)

## Plugin System

Tools are discovered at compile time via Roslyn source generators. Add a tool by implementing `ITool` with the `[ToolMetadata]` attribute:

```csharp
[ToolMetadata(
    Name = "my_tool",
    Description = "Does something useful",
    Category = ToolCategory.Information)]
public class MyTool : ITool
{
    public Task<ToolResult> ExecuteAsync(string input, CancellationToken ct) { ... }
}
```

Built-in tools: `wiki_query`, `web_search`, `reminder`, `file_system`

## Backup & Restore

```bash
# Backup wiki
./scripts/wiki-backup.sh backup

# List backups
./scripts/wiki-backup.sh list

# Restore from backup
./scripts/wiki-backup.sh restore data/backups/LeanKernel-wiki-20260429.tar.gz
```

## Resource Requirements

Designed for constrained hardware (4GB+ mini PC / NAS):

| Service | Memory Limit | Memory Reserved |
|---------|-------------|-----------------|
| LeanKernel Engine | 512 MB | 256 MB |
| Qdrant | 512 MB | 128 MB |
| LiteLLM | 256 MB | — |
| **Total** | **~1.3 GB** | |

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

## License

Private — All rights reserved.
