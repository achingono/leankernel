# Phase 2026-07-31 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Remote DB credentials (never displayed) | `~/source/repos/swarm/deploy/platform/secrets/postgres_user.txt`, `postgres_password.txt` | Platform/ops |
| Remote host | `192.168.*.*:5432`, Postgres 16.14, DB `leankernel` (owner `leankernel_app`); GBrain tables live in this DB | Verified 2026-07-31 |
| Local target DB | `database:5432` (`localhost:5432`), DB `gbrain`, user `leankernel`, password `leankernel-dev-password` (compose defaults) | `docker-compose.yml` |
| Local import-target identity mapping | Local `leankernel` DB `Tenants`/`Users`/`Channels`; explicit operator-approved target tenant/person/user plus discovered active channels | Verified 2026-07-31 (below) |
| Current slug conventions | `src/Services/LeanKernel.Services.Common/Memory/GBrainMemoryClient.cs` (`memory/{tenant}/{person}/{channel}/{key}`), `GBrainDocumentStoreClient.cs` (`documents/{tenant}/{scope}/{channel}/{user}/{fingerprint}`) | Engineering |
| Memory policy defaults | `src/Services/LeanKernel.Services.Gateway/appsettings.json` `Agents:Channels:MemoryPolicyDefaults` = `Share:["*"]`, `Access:["*"]` → all channels read everything | Engineering |
| Current document tool/runtime behavior | `DocumentSearchTool.cs`, `DocumentListTool.cs`, `GBrainDocumentStoreClient.cs` — tools already derive readable channels from policy, but the store currently searches only the request channel namespace and post-filters results; imported docs should instead use canonical user scope with channel-authorized retrieval | Engineering |
| GBrain CLI | Local `gbrain` container (`gbrain 0.42.67.0`); `migrate embeddings --to <p:model>` for re-embedding | Local stack |

## Verified Local Import-Target Mapping (current dev environment)

The current environment has one verified target mapping, but the implementation must treat these values as inputs rather than hard-coded constants. The script must accept or derive the target tenant/person/user and discover the active local channel set at runtime before applying the rewrite.

| Role | GUID | Source |
|---|---|---|
| Tenant (Default Tenant) | `e44ca455-2b3b-45f7-9ac1-3d77b6e66381` | `Tenants` |
| Person (active dev person) | `7d1282a7-e183-4457-8d56-41a5a393a0b1` | `Users` (user `0a6e0986-9c59-469c-a65a-c22c11f513dd`, Alfero Chingono); current local memory pages use this person |
| User | `0a6e0986-9c59-469c-a65a-c22c11f513dd` | `Users` |
| Channel openai-http | `9ff45fac-6334-47f2-ad0e-0f6cf03497a3` | `Channels` |
| Channel signal | `1d8ecc10-e21d-4256-a839-ee1bb3057054` | `Channels` |
| Channel teams | `3d86fc69-6cf2-4ceb-a2be-157d74d9e057` | `Channels` |

## Verified Remote Inventory (2026-07-31)

| Data | Value |
|---|---|
| `pages` total | 3,148 (documents 1,589; concepts 1,435; wiki 122; notes 2) |
| `learning/facts/%` | 1,434 (type `concept`; shape `learning/facts/{remoteTenant}/{remotePerson}/{seq}`; 13 remote tenants, 297+ distinct persons; content is `# Learned Fact` markdown) |
| `doc/%` | 1,589 (type `document`; raw title slugs, no identity; content is raw markdown) |
| Raw slugs (rest) | 125 (`what/*`, `how/*`, `identity-agent-main`, `identity-user-default`, `learning/engagement-metrics`, …) |
| `content_chunks` | 5,261 (all embedded) |
| `page_versions` | 1,449 (FK `page_id`, no slug column) |
| `tags` | 1,942 (FK `page_id` + `tag`, no slug column) |
| `page_aliases` | 94 (schema to inspect at implementation) |
| `links`, `files`, `facts` | 0 |
| Remote embedding config | `config` row says `embedding_model = openai:embedding-small`, dims 3072, `version 116` — **stale**: actual vectors were produced by `zeroentropyai:zembed-1` (all 5,261 chunks stamped `zeroentropyai:zembed-1`; gbrain's compile-time `DEFAULT_EMBEDDING_MODEL` per `postgres-engine.ts:2462`) |
| Local embedding config | `config` row `embedding_model = openai:embedding`, dims 3072, `version 125`; LiteLLM `embedding` route → Azure `text-embedding-3-large-1` (order 1) / `gemini-embedding-2` (fallback); local chunks stamped `openai:embedding` (8 rows) |
| Embedding compatibility verdict | **Not compatible despite equal 3072 dims** — remote vectors live in the ZeroEntropy zembed-1 space; local queries embed in the OpenAI text-embedding-3-large space. gbrain `searchVector` (`postgres-engine.ts:2126-2280`) compares query vs stored vectors with raw cosine distance and **no model/provenance guard** → mixed-model vectors would silently degrade retrieval. Re-embed required |
| Schema parity | `pages`/`content_chunks` columns identical local vs remote (ordering only differs) |
| Local triggers | `trg_pages_search_vector` and `chunk_search_vector_trigger` rebuild tsvectors on INSERT/UPDATE; embeddings are NOT trigger-maintained |

## Optional Inputs
- `gbrain` CLI surface for re-embedding (`migrate embeddings --to`, `list`, `get`, `search`, `stats`).
- Prior import notes from this plan's exploration session (see `evidence.md`).

## Input Validation Checklist
- [x] Remote connectivity verified; credentials read from files only
- [x] Remote inventory captured; local target DB verified writable
- [x] Slug conventions verified against current runtime builders
- [x] Identity mapping validated against local `Tenants`/`Users`/`Channels`
- [x] Plan records that target identity/channel values are operator inputs and active channels must be discovered before import
