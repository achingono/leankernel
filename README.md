# 🔷 LeanKernel — Lean Personal AI Agent

A high-performance, cost-optimized personal AI agent built on **.NET 10 / C# 14**. LeanKernel prioritizes **token efficiency** through tight context management and a modular backend.

## Architecture

LeanKernel is organized into three decoupled subsystems:

| Component | Role |
|-----------|------|
| **Commander** | Channel adapters (Signal, future Telegram/Discord). Routes messages. |
| **Thinker** | LLM reasoning via Semantic Kernel. Prompt assembly, tool dispatch, agent orchestration. |
| **Archivist** | Memory & context gatekeeper. 5W1H wiki, vector search, deny-by-default context gating. |

### Context Gatekeeper (Core Differentiator)

Unlike typical agents that send full conversation history to the LLM, LeanKernel's Archivist starts from **nothing** and only injects LeanKernels that earn their place:

```
[Query] → Intent Classification → 5W1H Wiki + Vector Search → Competitive Ranking → Budget Fill → Minimal Prompt
```

Token budget allocation: System Prompt 15% · Wiki Facts 20% · History 40% · RAG LeanKernels 20% · Tools 5%

## Stack

- **.NET 10** / C# 14 with System.Text.Json source generation
- **Semantic Kernel** for LLM orchestration
- **LiteLLM** proxy for model-agnostic routing (OpenAI, Anthropic, Ollama)
- **Qdrant** vector database for semantic search
- **signal-cli** for Signal messaging
- **Docker Compose** for deployment

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

## Project Structure

```
LeanKernel/
├── docker-compose.yml          # 3 services: engine, litellm, qdrant
├── Dockerfile                  # Multi-stage .NET 10 build
├── config/litellm/config.yaml  # LLM model routing
├── data/wiki/                  # 5W1H knowledge filesystem
└── src/
    ├── LeanKernel.Core/             # Interfaces, models, config
    ├── LeanKernel.Commander/        # Channel adapters (Signal)
    ├── LeanKernel.Thinker/          # LLM reasoning engine
    ├── LeanKernel.Archivist/        # Memory & context gatekeeper
    ├── LeanKernel.Scheduler/        # Cron-based proactive tasks
    ├── LeanKernel.Plugins/          # Tool/plugin system
    ├── LeanKernel.Generators/       # Roslyn source generators
    ├── LeanKernel.Host/             # Application entry point
    └── LeanKernel.Tests.*/          # Unit & integration tests
```

## License

Private — All rights reserved.
