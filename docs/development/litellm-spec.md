# LiteLLM Source Spec and Compiler

LeanKernel uses a **single authored source spec** at `config/litellm/config.yaml`.  
At container startup, `config/litellm/render_litellm_config.py` compiles that spec into the runtime LiteLLM config consumed by the proxy process.

## Runtime Flow

1. Load source spec (`/app/litellm_spec.yaml`)
2. Validate structure (providers, models, keys/base URLs, routes, aliases, router settings)
3. Resolve enabled key slots from environment variables
4. Expand routes into LiteLLM `model_list` deployments
5. Emit aliases and router fallback policies
6. Write rendered config (`/tmp/litellm_config.yaml`)
7. Start LiteLLM with rendered config

`config/litellm/Dockerfile` command:

```bash
python3 /app/render_litellm_config.py /app/litellm_spec.yaml /tmp/litellm_config.yaml \
  && exec litellm --config /tmp/litellm_config.yaml --port 4000 --num_workers ${LITELLM_NUM_WORKERS:-2}
```

## Source Spec Shape

Top-level sections used by the compiler:

- `providers` (keys/base_url refs + model catalog)
- `routes` (ordered provider/model/key chains per route)
- `aliases` (public model names -> route names)
- `router` (retry, cooldown, timeout, fallback behavior)
- optional: `general_settings`, `litellm_settings`

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
```
