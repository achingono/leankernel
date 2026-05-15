# Skills

This section covers the LeanKernel runtime skill system — how skills are defined, discovered, loaded, and executed.

## Contents

| Document | Description |
|----------|-------------|
| [skill-format.md](skill-format.md) | Implemented `SKILL.md` frontmatter contract and runtime execution behavior. |
| [runtime-skills-plan.md](runtime-skills-plan.md) | Roadmap/remediation notes for future skill system hardening. |

## How Skills Work

```mermaid
flowchart LR
    subgraph Filesystem
        SkillDir["data/skills/<name>/SKILL.md"]
    end

    subgraph Runtime
        Parser[SkillParser]
        Registry[RuntimeSkillRegistry]
        Factory[DynamicSkillToolFactory]
        Tool[DynamicSkillTool]
    end

    subgraph Agent
        Orchestrator[AgentOrchestrator]
        Worker[WorkerAgent]
    end

    SkillDir --> Parser
    Parser --> Registry
    Registry --> Factory
    Factory --> Tool
    Tool --> Worker
    Worker --> Orchestrator
```

## Skill Discovery Paths

Skills are loaded from configured `LeanKernel:Skills:BasePaths` directories (comma-separated).  
Default in appsettings is `/app/data/skills`.

Each configured path is scanned recursively for `SKILL.md`.

## Built-in Tools

The following tools are always available without a `SKILL.md`:

| Tool (Class/Model Name) | Capability |
|------------------------|-----------|
| `KnowledgeSearchTool` / `search_knowledge` | Primary semantic search over unified memory (wiki + documents) |
| `WikiQueryTool` / `search_wiki` | Semantic search narrowed to wiki content only |
| `DocumentSearchTool` / `search_documents` | Semantic search narrowed to documents only |
| `GetWikiEntryTool` / `get_wiki_entry` | Deterministic structured wiki lookup via `IWikiStore` |
| `WebSearchTool` / `web_search` | Web search integration |
| `FileSystem*`, `Directory*` tools | Read/search/mutate files under the configured data directory |

> **Note:** The left side is the internal tool class name (used in code and logs); the right side is the tool name exposed to the model and used in skill definitions and API calls. Always use the model-exposed name (e.g. `search_documents`) in prompts, skills, and API requests.
