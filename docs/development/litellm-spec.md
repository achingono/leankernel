# LiteLLM Source Spec and Compiler

LeanKernel uses a **single authored source spec** at `config/litellm/config.yaml`.  
At container startup, `config/litellm/render_litellm_config.py` compiles that spec into the runtime LiteLLM config consumed by the proxy process.

## Runtime Flow

1. Load source spec (`/app/config/litellm/config.yaml`)
2. Validate structure (providers, models, keys/base URLs, routes, aliases, router settings)
3. Resolve enabled key slots from environment variables
4. Expand routes into LiteLLM `model_list` deployments
5. Emit aliases, router fallback policies, and LiteLLM callback settings
6. Write rendered config (`/app/config/litellm/litellm_config.generated.yaml`)
7. Start LiteLLM with rendered config

`config/litellm/Dockerfile` command:

```bash
python3 /app/render_litellm_config.py /app/config/litellm/config.yaml /app/config/litellm/litellm_config.generated.yaml \
  && exec litellm --config /app/config/litellm/litellm_config.generated.yaml --port 4000 --num_workers ${LITELLM_NUM_WORKERS:-2}
```

## Source Spec Shape

Top-level sections used by the compiler:

- `providers` (keys/base_url refs + model catalog)
- `routes` (ordered provider/model/key chains per route)
- `aliases` (public model names -> route names)
- `router` (retry, cooldown, timeout, fallback behavior)
- optional: `general_settings`, `litellm_settings`

`litellm_settings.callbacks` is preserved by the compiler, and LeanKernel now injects a LiteLLM custom callback by default so route monitoring happens at the proxy layer for every LiteLLM request.

## Proxy-Layer Route Monitoring

The LiteLLM container now includes `config/litellm/leankernel_litellm_callbacks.py`, registered through the rendered LiteLLM config.

For each routed LiteLLM request, the callback records the proxy-observed route decision using LiteLLM's normalized routing metadata:

- requested public model alias
- routed provider
- deployment model id
- API base
- response model name when available

Artifacts written by the callback inside the container:

- route events: `/app/logs/litellm-route-events.jsonl`
- sync status: `/app/logs/litellm-limit-sync-status.json`
- drift report: `/app/logs/litellm-model-limit-drift.json`

In compose, these are persisted through `./data/logs:/app/logs`.

The LiteLLM auth/admin endpoints require a connected proxy DB in current LiteLLM builds. Compose now runs a dedicated Postgres sidecar (`litellm-db`) and sets:

- `DATABASE_URL=postgresql://...@litellm-db:5432/litellm`
- volume: `litellm-db-data:/var/lib/postgresql/data`

so API-key auth checks and proxy state survive container restarts.

## Off-Hours Restart Job

The proxy callback worker includes a scheduled off-hours restart check. If the source spec mtime advances, the job waits for the off-hours window and then exits the process so Docker restarts the `litellm` container.

Environment controls:

- `LITELLM_OFF_HOURS_RESTART_ENABLED` (default `true`)
- `LITELLM_OFF_HOURS_RESTART_CHECK_SECONDS` (default `300`)
- `LITELLM_OFF_HOURS_RESTART_WINDOW_START_HOUR` (default `2`)
- `LITELLM_OFF_HOURS_RESTART_WINDOW_END_HOUR` (default `5`)
- `LITELLM_OFF_HOURS_RESTART_STATE_PATH` (default `/app/logs/litellm-offhours-restart-state.json`)

## Environment-Gated Deployments

A route deployment is emitted only when required env vars for that key slot are present and non-empty.  
This lets one shared spec support multiple environments without editing YAML per environment.

## Local Render Preview

```bash
python3 config/litellm/render_litellm_config.py \
  config/litellm/config.yaml \
  /tmp/litellm_config.yaml
```

## Model Limit Sync Utility

`scripts/sync_litellm_model_limits.py` can update `max_tokens` and `context_window` in the source spec from provider metadata (Gemini, Groq, Azure).

```bash
# dry-run (default)
python3 scripts/sync_litellm_model_limits.py

# write updates back to config/litellm/config.yaml
python3 scripts/sync_litellm_model_limits.py --write

# limit sync to providers actually observed by LiteLLM
python3 scripts/sync_litellm_model_limits.py --write --providers azure,groq
```

The LiteLLM callback debounces this sync path inside the LiteLLM container, so provider metadata refresh is no longer tied to the LeanKernel engine scheduler or `Routing.Enabled`.
