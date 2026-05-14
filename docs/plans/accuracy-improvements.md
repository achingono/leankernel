# PRD: LLM Wiki Extraction, Indexed Store, Documents Support, and Reranker (v2)

## Overview

This PRD upgrades the wiki extraction and storage system to produce high-quality, structured 5W1H facts while maintaining excellent human readability. It introduces a clean separation between **Wiki** (personal/user-specific) and **Documents** (books, articles, PDFs, etc.), adds a lightweight reranker for superior context quality, and strengthens anti-drift mechanisms.

**Key Improvements over previous version:**

- Stronger quality gates + reranking to combat drift and noise.
- Proper handling of unstructured documents (PDFs, EPUBs, Word) via existing Python indexer.
- Clear human/machine balance via hierarchical folders + clean prompt rendering.
- Separate search tools and Qdrant collections for wiki vs documents.

## Problem Statement

(Same as original + additions)
- Noisy, drifting wiki with low signal-to-noise.
- Poor retrieval precision leading to irrelevant context.
- No clean separation or tools for reference documents.
- Context sent to LLM often includes file paths or noisy facts.

### Goals

- High-accuracy, low-drift knowledge store.
- Excellent human readability (markdown) + machine precision (structured facts + indexed retrieval).
- Fast, relevant context assembly via reranking.
- Separate, well-organized handling of personal wiki and reference documents.
- Rebuildability and safety (markdown as source of truth).

### Non-Goals (v2)

- Full relational DB for wiki.
- C# writing directly to Qdrant.
- Advanced multi-language normalization.
- Real-time manual fact curation UI (Phase 2+).

### Recommended Folder Structure

**Wiki:**

```
data/wiki/
├── who/
├── what/
├── when/
├── where/
├── why/
├── how/
├── .LeanKernel/    # index.json, migration logs, quarantine/
└── quarantine/ (optional visible fallback)
```

**Documents:**
```
data/documents/
├── books/
├── articles/
├── research-papers/
├── personal-notes/
└── raw/                  # PDFs, EPUBs, .docx (unstructured)
```

## Functional Requirements

### FR-1 LLM Extraction & Quality Gates

(Keep core from original + strengthened input/output gates as discussed.)

### FR-2 Canonical Markdown Format

(Keep the excellent structure with YAML frontmatter + `## Summary` + `## Facts` + ```yaml

### FR-3 Documents Processing

- Raw PDFs/EPUBs/Word files stay in `data/documents/raw/`.
- Python indexer (`indexer.py`) processes them using `unstructured.io`.
- Stores chunks in a dedicated **Qdrant collection** (`documents`).
- Metadata per chunk (example below).

**Recommended Documents Metadata:**

```json
{
  "source_type": "document",
  "filename": "atomic-habits.pdf",
  "file_type": "pdf",
  "chunk_index": 23,
  "page_number": 67,
  "title": "Atomic Habits",
  "author": "James Clear",
  "indexed_at": "2026-05-13T...",
  "tags": ["productivity", "habits"]
}
```

### FR-4 Reranker (New – High Impact)

After Qdrant returns top 20-30 candidates (from wiki or documents), apply a **lightweight local LLM reranker**:

- Use a small/fast model (Phi-3, Gemma-2B, etc.) via Ollama/LM Studio.
- Simple relevance scoring prompt.
- Keep only top 6-8 highest-scoring items for final context.
- This dramatically reduces noise and drift.

### FR-5 Search Tools

Two dedicated tools:

- `search_wiki` — personal facts, preferences, projects.
- `search_documents` — books, articles, reference material.

### FR-6 Context Assembly & Prompt Rendering

When injecting into the final prompt:

- Use clean, human-readable format (no file paths).
- Example:
  ```
  **Alfero Chingono** (who)
  • Prefers concise, direct assistance.
  • Works primarily from Toronto.

  **Atomic Habits** (document)
  • Small improvements compound over time.
  ```

## System Prompt Template (Recommended)

```markdown
You are a precise and helpful assistant.

**Relevant Context:**
{context}

**Knowledge Sources:**
- Wiki: Personal facts, preferences, projects, and rules about the user.
- Documents: Books, articles, and reference materials.

**Rules:**
- Base your responses primarily on the provided context.
- If you need more information, use the appropriate tool:
  - `search_wiki` for user-specific or personal information.
  - `search_documents` for general knowledge from books/articles.
- Do not guess or hallucinate facts. Ask the user if needed.
- Be concise, accurate, and direct.
```

## Architecture Updates

- **WikiStore** → Indexed, file-backed (as in original).
- **Python Indexer** → Handles both wiki markdown (fact-level) and documents (chunk-level).
- **Qdrant Collections**:
  - `wiki` (fact-level points).
  - `documents` (chunk-level points).
- **Reranker Service** → Pluggable `IReranker` (default: LocalLlmReranker).
- **ContextCandidateRetriever** → Retrieval → Rerank → Render clean context.

## Tool Definitions (JSON Schema)

**search_wiki:**
```json
{
  "name": "search_wiki",
  "description": "Search the personal wiki for user-specific facts, preferences, projects, or rules.",
  "parameters": {
    "type": "object",
    "properties": {
      "query": { "type": "string", "description": "Specific information needed from the wiki" }
    },
    "required": ["query"]
  }
}
```

**search_documents:**
```json
{
  "name": "search_documents",
  "description": "Search books, articles, and reference documents.",
  "parameters": {
    "type": "object",
    "properties": {
      "query": { "type": "string", "description": "Topic or information needed from documents" }
    },
    "required": ["query"]
  }
}
```

## Implementation Recommendations

- Add `IReranker` interface and `LocalLlmReranker` implementation early.
- Update `ContextCandidateRetriever` to use reranker by default.
- Run migration as one-shot (keep original plan).
- Add telemetry for reranker acceptance rate and average score.

## Rollout Phases (Updated)

1. Core extraction + indexed WikiStore (original Phases 0-2).
2. Documents indexing + separate Qdrant collection.
3. Reranker + updated ContextCandidateRetriever.
4. New tools + system prompt.
5. Migration + cleanup of old `llm/` folder.
6. Validation and tuning.
