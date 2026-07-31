# Handoff — 2026-07-31 gbrain import runtime smoke tests

Owner: prior model (import session). Reader: next model continuing the runtime smoke-test / R11 closure work.

## 1. State Summary

- The real gbrain import is **complete and validated** (restore, rewrite, re-embed, backups, plan docs).
- Runtime smoke tests through the gateway (`/v1/responses` as bound user `cumbersome/user@example.com` → user `0a6e0986-…`):
  - Identity resolution: **works** — model replies "Alfero Chingono" with the stored channel binding token.
  - `memory_search` (Category A learning fact + Category C network concept): **works** — imported `memory/…/learning/facts/…` and `memory/…/how/network` surfaced correctly.
  - `document_list`: **FAILS (returns [])** — two compounding causes (below).
  - `document_search`: **leaks non-document slugs** — the deployed gateway is running a **stale build** without the R11 store-client filtering (container DLL built 2026-07-30 22:08; `GBrainDocumentStoreClient.cs` last modified Jul 31 09:26). Raw tool output contained `memory/…` slugs; the model then reported a memory page ("Miovision Update") as the best document match.

## 2. Root Causes (verified)

1. **Stale gateway deployment**: `leankernel-gateway` container image predates the R11 document-retrieval changes. Everything in section 1 is suspect until the gateway is rebuilt and redeployed. Evidence: `docker exec leankernel-gateway stat /app/LeanKernel.Services.Common.dll` → `2026-07-30 22:08` vs source mtime `Jul 31 09:26`.
2. **`document_list` would still fail on a fresh build**: `ListAsync` in `src/Services/LeanKernel.Services.Common/Memory/GBrainDocumentStoreClient.cs:112` calls `list_pages` with `{ sort = "updated_desc", limit = 100 }` **without a type filter**. The imported brain has 1,566 memory pages with newer `updated_at` than the 286 documents, so the top-100 window is 99 memory + 1 probe page, **0 documents** (verified live: `tools/call list_pages limit=100` → `{'memory': 99, '__lk_probe_write__': 1}`). The `IsReadable` filter then drops everything.
   - **Verified fix**: `list_pages` accepts `type` filter (and `offset` for pagination). `{"type": "document", "sort": "updated_desc", "limit": 100}` returns 100 user-scoped `documents/e44ca455-…/user/00000000-…/0a6e0986-…/{fp}` pages. Documents' `type` column = `document`; memory pages are `concept`/`wiki`/`note`.
   - Note: 286 unique documents > the 100 per-call remote cap → pass `type` AND paginate with `offset` in the fix (3 calls for 286).

## 3. Remaining Tasks (in order)

1. **Fix `ListAsync`** in `GBrainDocumentStoreClient.cs:112` to call `list_pages` with `type = "document"` and paginate via `offset` (fetch `ceil((limit*3)/100)` pages; keep the existing `IsReadable` + fingerprint-merge + ordering logic). Add/adjust unit tests in `GBrainDocumentStoreClientTests` (13 exist for R11).
2. **Rebuild + redeploy the gateway**: `docker compose up -d --build gateway` (or equivalent for the local stack). Verify the new DLL mtime in the container.
3. **Re-run the four smoke probes** (commands in section 5) and confirm:
   - `document_list` returns imported documents (titles like 01-mission),
   - `document_search "mission"` returns only `documents/…/{fp}` slugs (no memory/`__lk_probe_write__` leaks).
4. **Close out docs**:
   - Append smoke results + the `document_list` type-filter finding to `docs/plans/2026-07-31-gbrain-import-to-local/evidence.md`.
   - Mark exit-criteria item "gateway memory + `document_search`/`document_list` smoke tests" checked once green.
   - Update risk-register R11 → Closed (runtime validation done).
5. **Optional cleanup**: gbrain container `~/.gbrain/config.json` still points `database_url` at `gbrain_import_staging` (from the staging session). The import script now pins `GBRAIN_DATABASE_URL` per run, so this is latent-only, but the config should be pointed back at `gbrain` to avoid confusing future ad-hoc CLI runs.

## 4. Environment Insights (this session)

- `leankernel-gateway` was in a **13-hour unhealthy hot loop** (99.9% CPU, empty HTTP replies) from a DB startup race at container start (`57P03 the database system is starting up`). Fixed with `docker restart leankernel-gateway`; health endpoint `/health` → 200.
- Stored channel binding tokens **still validate after the gateway restart** (name probe returned "Alfero Chingono"). Note the general durability caveat: `Identity:Token:SecretKey` is empty (appsettings.json), so `JwtSecurityTokenGenerator` uses a per-process random `DevSecretKey` — see AGENTS.md + ADR 0006. If a future restart invalidates tokens, terminals fall silently to the guest/anonymous path (model will say "your name is anonymous").
- gbrain MCP (brain-wide search, `ns` ignored): `http://localhost:8789/mcp`, JSON-RPC, **requires `Authorization: Bearer <engine-token>`**; token file lives in the gateway container at `/app/data/gbrain/.engine-token` (71 bytes). `tools/list` shows `list_pages` supports `type`, `tag`, `sort`, `limit`, `offset`; remote callers capped at `limit=100`.
- Gateway `/v1/responses` request shape (must include agent name): `{"model":"medium","input":"…","agent":{"name":"leankernel"}}` with `Authorization: Bearer <binding token>`. Missing `agent` → `invalid_request_error: No 'agent.name' or 'metadata["entity_id"]' specified`.
- Binding tokens are read from DB (never echoed): `select "BearerToken" from "ChannelSenderBindings" where "Issuer"='cumbersome' and "Subject"='user@example.com' and "IsActive";` (token ~1096 chars). Channel claim join is by channel **name** (`openai-http`), claims: `lk_sender_iss`, `lk_sender_sub`, `lk_channel`, `lk_tenant_id` (`Constants.cs:359-374`).
- 5 active bindings in local DB: signal `+16474050515`, and 4 under channel `openai-http` (`9ff45fac-…`); user `0a6e0986-…` = user@example.com = the import user.
- Model backend: `model=medium` via LiteLLM (`OPENAI__BASEURL=http://litellm:4000/v1`); gbrain container env has `OPENAI_API_KEY`/`OPENAI_BASE_URL` empty by default — export LiteLLM values for embed/CLI work.

## 5. Smoke Probe Commands (reusable)

```bash
TOKEN=$(docker exec -i leankernel-database psql -U leankernel -d leankernel -tAc "select \"BearerToken\" from \"ChannelSenderBindings\" where \"Issuer\"='cumbersome' and \"Subject\"='user@example.com' and \"IsActive\";")

# identity probe (expect "Alfero Chingono")
curl -s --max-time 180 -X POST http://localhost:8080/v1/responses -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"model":"medium","input":"What is your name? Reply with only your full name.","agent":{"name":"leankernel"}}'

# memory_search Category A (learning fact)
curl -s --max-time 240 -X POST http://localhost:8080/v1/responses -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"model":"medium","input":"Search your memory for anything you learned about the job posting that uses Workwolf AI tools to sort and screen applications. Quote what your memory says.","agent":{"name":"leankernel"}}'

# memory_search Category C (network concept)
curl -s --max-time 240 -X POST http://localhost:8080/v1/responses -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"model":"medium","input":"Search your memory: what does your network page say about what a relationship is FOR? Summarize the key idea.","agent":{"name":"leankernel"}}'

# document_list (currently FAILS; re-test after fix + redeploy)
curl -s --max-time 240 -X POST http://localhost:8080/v1/responses -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"model":"medium","input":"List my documents using the document_list tool, then name the first few document titles you see.","agent":{"name":"leankernel"}}'

# document_search (currently leaks memory slugs; re-test after redeploy)
curl -s --max-time 240 -X POST http://localhost:8080/v1/responses -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"model":"medium","input":"Use the document_search tool to search your documents for content about mission. Then tell me the title of the best matching document.","agent":{"name":"leankernel"}}'

# direct gbrain list_pages check (bypasses gateway)
ENGINE_TOKEN=$(docker exec leankernel-gateway sh -c 'cat /app/data/gbrain/.engine-token')
curl -s --max-time 30 -X POST http://localhost:8789/mcp -H "Authorization: Bearer $ENGINE_TOKEN" -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"list_pages","arguments":{"type":"document","sort":"updated_desc","limit":100}}}'
```

## 6. Session Artifacts

- Smoke outputs: `/tmp/smoke-name.json`, `/tmp/smoke-memA.json`, `/tmp/smoke-memC.json`, `/tmp/smoke-doclist.json`, `/tmp/smoke-docsearch.json`, `/tmp/listpages.json`, `/tmp/listpages-doc.json`
- Import backups/manifests: `scripts/gbrain/backups/` (pre/post-import, `remote-gbrain-20260731-103734.sql.gz`, manifests under `manifests/`)
- Import script + SQL: `scripts/gbrain/import-remote.sh`, `scripts/gbrain/sql/` (incl. `restore-real.sql` — id-offset restore, name-based column mapping, idempotent)
- Plan docs: `docs/plans/2026-07-31-gbrain-import-to-local/{evidence,exit-criteria,risk-register,outputs}.md` (import side closed; runtime smoke items above remain)
