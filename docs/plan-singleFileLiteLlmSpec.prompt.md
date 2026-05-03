## Plan: Single-File LiteLLM Spec

Replace the current hand-expanded LiteLLM template with one human-authored source spec that is compiled at container startup into the final LiteLLM YAML. The recommended source format is a single declarative YAML spec, not JSON, because this repo already leans on YAML/Docker workflows and the user explicitly prefers a single file. The new spec should let contributors edit five concepts directly: providers, provider keys, provider models, tiers, and tier-model bindings. The generator should preserve all app-facing contracts by continuing to emit OpenAI-compatible model aliases and a standard LiteLLM `model_list`/`router_settings` output.

**Steps**
1. Phase 1: Replace the current authoring model in `/Users/achingono/source/repos/LeanKernel/config/litellm/config.yaml` with a single source-spec format stored alongside the existing LiteLLM config assets. The source spec should expose these top-level sections:
   - `providers`: provider families such as groq, gemini, azure, local
   - `provider_keys`: named key instances per provider, including required env vars, api_base env vars when applicable, and order/priority
   - `provider_models`: model catalog per provider family, so model identifiers are defined once and reused by tiers
   - `tiers`: tier definitions containing model-info profile plus tier routes that reference provider keys and provider model ids
   - `aliases`: external model names like `gpt-4o-mini` and `gpt-4o` mapped to tiers
   - `router`: retry, cooldown, fallback, and status-code policy
2. Phase 1 detail: Use references instead of duplication. A tier route should say, in effect, “for tier1 use provider groq with keys groq1/groq2 and model scout,” rather than expanding one block per key in source. The generator should expand that into one LiteLLM deployment entry per key at runtime.
3. Phase 1 detail: Keep provider-key metadata explicit in source so the current split logic inside Python is no longer duplicated across files. Required env vars should live next to each key definition, not in a separate marker map.
4. Phase 2: Rewrite `/Users/achingono/source/repos/LeanKernel/config/render_litellm_config.py` from a line-based filter into a structured compiler:
   - load the source spec
   - validate required sections and references
   - resolve available provider keys from environment
   - expand tiers into LiteLLM `model_list` entries
   - emit alias groups and router settings
   - write the rendered LiteLLM YAML to `/tmp/litellm_config.yaml`
5. Phase 2 detail: Derive repetitive output automatically where the rules are stable. Recommended approach:
   - derive alias deployment groups from the `aliases` map
   - derive provider availability from `provider_keys[*].required_env`
   - keep tier-to-tier fallback order explicit in source so behavior remains reviewable
6. Phase 2 detail: Add strict validation in the compiler so configuration errors fail fast before LiteLLM starts. Minimum checks:
   - referenced provider keys exist
   - referenced provider models exist for the provider family
   - aliases point to valid tiers
   - fallback targets point to valid tiers
   - orders are numeric and comparable
7. Phase 3: Update `/Users/achingono/source/repos/LeanKernel/config/litellm/Dockerfile` so the container copies the new source spec and runs the structured compiler before starting LiteLLM. Preserve the current runtime-only generation pattern; do not commit the compiled YAML to git.
8. Phase 3 detail: Keep `/Users/achingono/source/repos/LeanKernel/docker-compose.yml` mostly unchanged at the interface boundary. It should continue passing provider env vars into the LiteLLM container, but it should not need schema-specific knowledge beyond those env vars.
9. Phase 4: Preserve application compatibility. The new generator must continue emitting all model aliases and direct model names that the app depends on, because the C# side does not understand tiers. Preserve these constraints:
   - `LEANKERNEL__LiteLlm__DefaultModel` remains a free-form string that resolves against LiteLLM output
   - `LEANKERNEL__LiteLlm__EmbeddingModel` remains separate from chat model selection
   - the app continues to call a single model name, not a tier selection API
10. Phase 4 detail: Treat embeddings as an adjacent compatibility surface even though they are not the core authoring focus. At minimum, the redesign must preserve an explicit way to emit embedding-capable model entries so `/v1/embeddings` continues to work. Recommended extension: add an `embeddings` section to the source spec with model name, provider reference, and expected dimension metadata.
11. Phase 5: Update `/Users/achingono/source/repos/LeanKernel/README.md` and `/Users/achingono/source/repos/LeanKernel/.env.example` to document the new single-source authoring flow, the provider key/env model, and a local preview command for rendering the final LiteLLM YAML before container startup.
12. Phase 5 detail: Keep the migration incremental. Start by generating exactly the same LiteLLM YAML shape the repo uses today, then clean up fallback rules or add richer metadata only after parity is verified.

**Relevant files**
- `/Users/achingono/source/repos/LeanKernel/config/litellm/config.yaml` — current hand-expanded LiteLLM template; main source of duplication to replace
- `/Users/achingono/source/repos/LeanKernel/config/render_litellm_config.py` — current line-based filter; should become a structured source-spec compiler
- `/Users/achingono/source/repos/LeanKernel/config/litellm/Dockerfile` — runtime entrypoint for render-before-start flow
- `/Users/achingono/source/repos/LeanKernel/docker-compose.yml` — passes provider env vars into the LiteLLM container
- `/Users/achingono/source/repos/LeanKernel/.env.example` — current source of provider env documentation
- `/Users/achingono/source/repos/LeanKernel/README.md` — current operator/developer docs
- `/Users/achingono/source/repos/LeanKernel/src/LeanKernel.Core/Configuration/LeanKernelConfig.cs` — preserves `DefaultModel`, `EmbeddingModel`, `ContextWindowTokens`, and `ApiKey` contracts
- `/Users/achingono/source/repos/LeanKernel/src/LeanKernel.Archivist/Embedding/EmbeddingService.cs` — embedding endpoint consumer; sensitive to embedding model and dimension compatibility
- `/Users/achingono/source/repos/LeanKernel/src/LeanKernel.Thinker/AgentFactory.cs` — chat client creation using a single configured model name
- `/Users/achingono/source/repos/LeanKernel/src/LeanKernel.Host/Services/OnboardingOrchestrator.cs` — onboarding validation flow that probes LiteLLM connectivity and embeddings

**Verification**
1. Render the source spec locally with a full env set and confirm the compiler produces valid LiteLLM YAML with all expected provider deployments, alias groups, and router settings.
2. Render again with selective env vars removed and confirm only the corresponding provider-key deployments disappear while unrelated providers remain intact.
3. Run `docker compose config` to confirm the container interface did not drift.
4. Start the LiteLLM service and confirm `/v1/models` includes the expected aliases used by the app, including the default chat model alias.
5. Run an embeddings probe against `/v1/embeddings` using the configured embedding model and confirm the generated config still supports the archivist/indexer path.
6. Smoke test the app path by keeping `LEANKERNEL__LiteLlm__DefaultModel` pointed at an alias rather than a tier name and confirming no C# code changes are required.

**Decisions**
- Use a single human-authored source file rather than split provider/tier files.
- Generate the final LiteLLM YAML only at runtime/build time; do not check generated YAML into git.
- Prefer a YAML source spec over JSON because it fits the repo’s existing Compose/config workflow while still supporting structured compilation.
- Keep tiers as a LiteLLM-internal abstraction; do not push tier selection into the C# application layer in the initial redesign.

**Further Considerations**
1. Embeddings should probably be modeled explicitly in the source spec instead of being treated as an unrelated one-off. Recommendation: add a top-level `embeddings` section so model name, provider reference, and expected dimension stay synchronized.
2. Alias expansion should be generated, not hand-authored per provider key. Recommendation: define aliases once and let the compiler emit the repeated LiteLLM entries.
3. Router fallbacks should stay partially explicit. Recommendation: keep tier-to-tier fallbacks visible in source, but derive alias fallbacks automatically from alias-to-tier mappings.